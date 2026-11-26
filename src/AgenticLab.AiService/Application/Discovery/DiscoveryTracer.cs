using System.Runtime.CompilerServices;

namespace AgenticLab.AiService.Application.Discovery;

/// <summary>
/// Orchestrates a discovery run across the requested discovery source(s) — MCP, A2A, or both — and
/// projects it into an ordered stream of <see cref="DiscoveryEvent"/>s for the visualization UI. A run
/// first tears down and re-runs each source's discovery (<see cref="McpToolProvider.RediscoverAsync"/> /
/// <see cref="A2AAgentProvider.RediscoverAsync"/>), then refreshes the built agents so the freshly
/// discovered tools take effect (<see cref="AgentCatalog.RefreshDiscoveryAgents"/>). Each step is gated on
/// a <see cref="FlowSession"/> so the client can pace, pause, single-step or stop the run — reusing the
/// same stepping mechanism as the agent flow. Concurrent runs are serialized so a re-discovery cannot race
/// another.
/// </summary>
/// <param name="mcp">The MCP tool discovery source.</param>
/// <param name="a2a">The A2A agent discovery source.</param>
/// <param name="catalog">The agent catalog whose discovery-backed agents are refreshed after a run.</param>
/// <param name="registry">The registry the run's <see cref="FlowSession"/> is removed from when it ends.</param>
/// <param name="configuration">Configuration, read for the <c>Discovery:OnStartup</c> flag in the snapshot.</param>
/// <param name="timeProvider">Clock for the on-demand retry interval; defaults to the system clock.</param>
public sealed class DiscoveryTracer(
    McpToolProvider mcp,
    A2AAgentProvider a2a,
    AgentCatalog catalog,
    FlowControlRegistry registry,
    IConfiguration configuration,
    TimeProvider? timeProvider = null)
{
    /// <summary>Minimum time between on-demand retries of the same failed source.</summary>
    public static readonly TimeSpan OnDemandRetryInterval = TimeSpan.FromMinutes(1);

    /// <summary>Upper bound on how long an on-demand retry may delay a chat run.</summary>
    public static readonly TimeSpan OnDemandRetryTimeout = TimeSpan.FromSeconds(10);

    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider time = timeProvider ?? TimeProvider.System;
    private DateTimeOffset _mcpRetryAfter = DateTimeOffset.MinValue;
    private DateTimeOffset _a2aRetryAfter = DateTimeOffset.MinValue;

    /// <summary>Whether discovery runs automatically at startup (config <c>Discovery:OnStartup</c>, default true).</summary>
    public bool DiscoverOnStartup => configuration.GetValue("Discovery:OnStartup", true);

    /// <summary>
    /// Runs discovery for the requested <paramref name="source"/> (<c>"mcp"</c>, <c>"a2a"</c>, or
    /// <c>"all"</c>/null for both) and yields each step as it happens, pacing each on
    /// <paramref name="session"/> and finishing with a <see cref="DiscoveryEventKind.Complete"/> event once
    /// the agents have been refreshed. Runs are serialized: if one is already in progress, this waits for it
    /// to finish before starting.
    /// </summary>
    /// <param name="source">The source to discover: <c>"mcp"</c>, <c>"a2a"</c>, or <c>"all"</c>/null for both.</param>
    /// <param name="session">The control session that paces, pauses and stops the run.</param>
    /// <param name="cancellationToken">Cancels the discovery run.</param>
    public async IAsyncEnumerable<DiscoveryEvent> StreamAsync(
        string? source,
        FlowSession session,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, session.StopToken);
        var token = linked.Token;

        var runMcp = string.IsNullOrWhiteSpace(source) || source is "all" or "mcp";
        var runA2a = string.IsNullOrWhiteSpace(source) || source is "all" or "a2a";

        await _gate.WaitAsync(token);
        try
        {
            var seq = 0;

            if (runMcp)
            {
                await foreach (var evt in mcp.RediscoverAsync(token))
                {
                    await session.WaitForStepAsync(token);
                    yield return evt with { Sequence = seq++ };
                }
            }

            if (runA2a)
            {
                await foreach (var evt in a2a.RediscoverAsync(token))
                {
                    await session.WaitForStepAsync(token);
                    yield return evt with { Sequence = seq++ };
                }
            }

            // Rebuild the agents that use the discovered tools so the rediscovery takes effect on them.
            catalog.RefreshDiscoveryAgents();

            await session.WaitForStepAsync(token);
            yield return new DiscoveryEvent("all", DiscoveryEventKind.Complete, "Discovery complete; agents refreshed.", Sequence: seq);
        }
        finally
        {
            _gate.Release();
            registry.Remove(session.Id);
        }
    }

    /// <summary>
    /// Before a chat run, re-runs discovery for the sources the named agent uses (MCP and/or A2A) whose last
    /// run <see cref="DiscoveryState.Failed"/>, at most once per <see cref="OnDemandRetryInterval"/> per
    /// source. Eligible sources are retried concurrently under one <see cref="OnDemandRetryTimeout"/>
    /// deadline (measured on the injected clock). This recovers agents whose server was not
    /// yet listening at startup (Radix and Compose start components without readiness ordering) or was down
    /// during a manual run, without a background loop. The discovery-backed agents are refreshed only when a
    /// source reconnects. Skipped while a manual discovery run is in progress; never throws for a failed retry.
    /// </summary>
    /// <param name="agentName">The requested agent name, or null/blank for the default.</param>
    /// <param name="cancellationToken">Cancels the retry (the chat request's token).</param>
    public async Task RetryFailedSourcesForAsync(string? agentName, CancellationToken cancellationToken)
    {
        var (usesMcp, usesA2a) = catalog.DiscoverySources(agentName);
        var mcpFailed = usesMcp && mcp.Status.State == DiscoveryState.Failed;
        var a2aFailed = usesA2a && a2a.Status.State == DiscoveryState.Failed;
        if ((!mcpFailed && !a2aFailed) || !await _gate.WaitAsync(0, cancellationToken))
        {
            return;
        }

        try
        {
            var now = time.GetUtcNow();
            using var deadline = new CancellationTokenSource(OnDemandRetryTimeout, time);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);

            // Both sources start before either is awaited, so a stalled one cannot use up the other's time
            // (and its cooldown) under the shared deadline.
            var attempts = new List<Task<bool>>(2);
            if (mcpFailed && now >= _mcpRetryAfter)
            {
                _mcpRetryAfter = now + OnDemandRetryInterval;
                attempts.Add(TryReconnectAsync(mcp.ConnectAsync, () => mcp.Status.State, timeout.Token, cancellationToken));
            }

            if (a2aFailed && now >= _a2aRetryAfter)
            {
                _a2aRetryAfter = now + OnDemandRetryInterval;
                attempts.Add(TryReconnectAsync(a2a.ConnectAsync, () => a2a.Status.State, timeout.Token, cancellationToken));
            }

            if ((await Task.WhenAll(attempts)).Any(recovered => recovered))
            {
                catalog.RefreshDiscoveryAgents();
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private static async Task<bool> TryReconnectAsync(
        Func<CancellationToken, Task> connect, Func<DiscoveryState> state, CancellationToken token, CancellationToken requestToken)
    {
        try
        {
            await connect(token);
        }
        catch (OperationCanceledException) when (!requestToken.IsCancellationRequested)
        {
            // Timed out: the source stays failed and is retried after the interval.
        }

        return state() == DiscoveryState.Connected;
    }

    /// <summary>
    /// Returns the current status of every discovery source without running discovery, so a client can
    /// render the last-known result. Also reports whether discovery runs at startup.
    /// </summary>
    public DiscoverySnapshot Snapshot() =>
        new(DiscoverOnStartup, [mcp.Status, a2a.Status]);
}
