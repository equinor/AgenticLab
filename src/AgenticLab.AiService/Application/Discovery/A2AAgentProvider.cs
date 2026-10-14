using System.ComponentModel;
using System.Net.Http.Json;
using A2A;
using Microsoft.Extensions.AI;
using AgenticLab.Extensibility.Examples;
using AgenticLab.Extensibility.Runtime;

namespace AgenticLab.AiService.Application.Discovery;

/// <summary>
/// Connects to a remote server that hosts one or more agents over the Agent2Agent (A2A) protocol and
/// exposes delegation tools the agents can call. Where <see cref="McpToolProvider"/>
/// discovers external <em>tools</em> over MCP, this provider reaches external <em>agents</em> over A2A: it
/// discovers the server's agents (their names, paths and descriptions), builds an <see cref="A2AClient"/>
/// per agent, and exposes a <c>DelegateToAgent(agentName, question)</c> tool that routes a question to
/// the named agent and returns its answer. Remote agents claimed by an enabled example's
/// <see cref="ExampleManifest.RemoteAgentNames"/> are excluded from the shared roster handed to core agents
/// (<see cref="GetTools()"/>); examples request their own bounded tool with <see cref="GetTools(IReadOnlyCollection{string})"/>.
/// Because the roster is discovered at startup, adding an unclaimed agent to the A2A server surfaces it
/// here with no code change. When the server is unavailable the provider degrades gracefully to an empty
/// tool list instead of failing the service.
/// </summary>
public sealed class A2AAgentProvider(IConfiguration configuration, ILogger<A2AAgentProvider> logger, ExampleCatalog? examples = null) : IDisposable, IAgentDelegation
{
    private readonly HttpClient _http = new();
    private readonly Dictionary<string, A2AClient> _clients = new(StringComparer.OrdinalIgnoreCase);
    private IList<AITool> _tools = [];
    private IReadOnlyList<A2AAgentInfo> _agentInfos = [];
    private IReadOnlyList<string> _sharedAgentNames = [];
    private DiscoverySourceStatus _status = new("a2a", null, DiscoveryState.NotRun, null, null, []);

    /// <summary>Whether at least one remote A2A agent was discovered.</summary>
    public bool IsConnected => _clients.Count > 0;

    /// <summary>The shared-roster delegation tool for core agents. Empty until discovery finds an unclaimed agent.</summary>
    public IList<AITool> GetTools() => _tools;

    /// <summary>
    /// A delegation tool bounded to the named discovered agents, in requested order; names that were not
    /// discovered are ignored and an empty list yields no tool. The tool's description and its unknown-agent
    /// reply mention only the bounded names.
    /// </summary>
    public IList<AITool> GetTools(IReadOnlyCollection<string> names)
    {
        var roster = names
            .Select(name => _agentInfos.FirstOrDefault(info => string.Equals(info.Name, name, StringComparison.OrdinalIgnoreCase)))
            .OfType<A2AAgentInfo>()
            .ToList();
        return roster.Count == 0 ? [] : [BuildDelegationTool(roster)];
    }

    /// <summary>The discovered A2A agents' names and descriptions, surfaced so clients can show what was discovered.</summary>
    public IReadOnlyList<A2AAgentInfo> AgentInfos => _agentInfos;

    /// <summary>The discovered agents no enabled example claims, i.e. the roster core agents may delegate to.</summary>
    public IReadOnlyList<string> SharedAgentNames => _sharedAgentNames;

    /// <summary>The current discovery status (endpoint, state, last-run time and discovered agents).</summary>
    public DiscoverySourceStatus Status => _status;

    /// <summary>
    /// Resolves the remote A2A server's endpoint (via service discovery, falling back to the <c>A2A:Endpoint</c>
    /// configuration value for a standalone run), discovers the agents it hosts and builds an
    /// <see cref="A2AClient"/> for each. Safe to call once at startup; failures are logged and leave the tool
    /// list empty so the agents still load. This is a convenience wrapper that drains <see cref="RediscoverAsync"/>.
    /// </summary>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        await foreach (var _ in RediscoverAsync(cancellationToken))
        {
            // Draining the stream performs the discovery; the events are only needed by streaming callers.
        }
    }

    /// <summary>
    /// Tears down any existing A2A clients and cached tools, then re-resolves the A2A server's endpoint,
    /// re-discovers the agents it hosts and rebuilds an <see cref="A2AClient"/> for each, yielding a
    /// <see cref="DiscoveryEvent"/> for each step so the process can be visualized. Failures are logged and
    /// leave the tool list empty so the agents still load.
    /// </summary>
    public async IAsyncEnumerable<DiscoveryEvent> RediscoverAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var seq = 0;
        yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Start, "Starting A2A agent discovery.", Sequence: seq++);

        // Clean up any previous clients and cached tools before rediscovering (the shared HttpClient is kept).
        _clients.Clear();
        _tools = [];
        _agentInfos = [];
        _sharedAgentNames = [];
        yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Cleanup, "Cleared previous A2A clients and cached tools.", Sequence: seq++);

        var baseEndpoint = configuration["services:a2aserver:http:0"]
            ?? configuration["services:a2aserver:https:0"]
            ?? configuration["A2A:Endpoint"];
        if (string.IsNullOrWhiteSpace(baseEndpoint))
        {
            logger.LogWarning("No A2A server endpoint configured (services:a2aserver:* or A2A:Endpoint); skipping A2A agent discovery.");
            _status = new DiscoverySourceStatus("a2a", null, DiscoveryState.NoEndpoint, null, DateTimeOffset.UtcNow, []);
            yield return new DiscoveryEvent("a2a", DiscoveryEventKind.NoEndpoint, "No A2A server endpoint configured; skipping discovery.", Sequence: seq++);
            yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Done, "A2A discovery finished (no endpoint).", Sequence: seq++);
            yield break;
        }

        _status = _status with { Endpoint = baseEndpoint, State = DiscoveryState.Connecting };
        yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Endpoint, $"Resolved A2A endpoint {baseEndpoint}.", Sequence: seq++);
        yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Connecting, "Requesting the A2A server's agent roster.", Sequence: seq++);

        IReadOnlyList<A2ADiscoveredAgent>? agents = null;
        string? error = null;
        try
        {
            var baseUri = new Uri(baseEndpoint);

            // Discover the agents this server hosts so the roster is not hard-coded here.
            var discovery = await _http.GetFromJsonAsync<A2ADiscoveryResponse>(
                new Uri(baseUri, "/agents"), cancellationToken);
            agents = discovery?.Agents ?? [];

            foreach (var agent in agents)
            {
                if (string.IsNullOrWhiteSpace(agent.Name) || string.IsNullOrWhiteSpace(agent.Path))
                {
                    continue;
                }

                _clients[agent.Name] = new A2AClient(new Uri(baseUri, agent.Path), _http);
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Could not connect to the A2A server at {Endpoint}; the orchestrator will run without A2A delegation.", baseEndpoint);
            error = ex.Message;
        }

        if (error is not null)
        {
            _clients.Clear();
            _status = new DiscoverySourceStatus("a2a", baseEndpoint, DiscoveryState.Failed, error, DateTimeOffset.UtcNow, []);
            yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Error, $"Could not reach A2A server: {error}", Sequence: seq++);
            yield break;
        }

        if (_clients.Count == 0)
        {
            logger.LogWarning("A2A server at {Endpoint} reported no agents; the orchestrator will run without A2A delegation.", baseEndpoint);
            _status = new DiscoverySourceStatus("a2a", baseEndpoint, DiscoveryState.Connected, null, DateTimeOffset.UtcNow, []);
            yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Done, "A2A discovery finished — no agents reported.", Sequence: seq++);
            yield break;
        }

        yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Listing, "Listing A2A agents.", Sequence: seq++);

        _agentInfos = agents!
            .Where(a => _clients.ContainsKey(a.Name))
            .Select(a => new A2AAgentInfo(a.Name, a.Description))
            .ToList();

        var items = new List<DiscoveryItem>();
        foreach (var info in _agentInfos)
        {
            var item = new DiscoveryItem(info.Name, info.Description, examples?.ForRemoteAgent(info.Name)?.Id);
            items.Add(item);
            yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Item, $"Discovered agent '{info.Name}'.", item, seq++);
        }

        // Core agents only see agents no enabled example claims; examples bound their own tool explicitly.
        var shared = _agentInfos.Where(info => examples?.ForRemoteAgent(info.Name) is null).ToList();
        _sharedAgentNames = shared.Select(info => info.Name).ToList();
        _tools = shared.Count == 0 ? [] : [BuildDelegationTool(shared)];

        _status = new DiscoverySourceStatus("a2a", baseEndpoint, DiscoveryState.Connected, null, DateTimeOffset.UtcNow, items);
        logger.LogInformation("Discovered {Count} A2A agent(s) at {Endpoint}: {Agents} (shared: {Shared}).",
            _clients.Count, baseEndpoint, string.Join(", ", _clients.Keys), string.Join(", ", _sharedAgentNames));
        yield return new DiscoveryEvent("a2a", DiscoveryEventKind.Done, $"A2A discovery finished — {items.Count} agent(s).", Sequence: seq++);
    }

    // The tool's description lists only its roster so the model knows who it can delegate to.
    private AITool BuildDelegationTool(IReadOnlyList<A2AAgentInfo> roster)
    {
        var allowed = new HashSet<string>(roster.Select(info => info.Name), StringComparer.OrdinalIgnoreCase);
        var names = string.Join(", ", roster.Select(info => info.Name));
        var summary = string.Join("; ", roster.Select(info => $"{info.Name} — {info.Description}"));
        return AIFunctionFactory.Create(
            async (
                [Description("The name of the specialist agent to delegate to.")] string agentName,
                [Description("The question or request to send to the agent.")] string question,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(agentName) || !allowed.Contains(agentName.Trim()))
                {
                    return $"Unknown agent '{agentName}'. Available agents: {names}.";
                }

                return (await InvokeAsync(agentName, question, cancellationToken)).Text;
            },
            new AIFunctionFactoryOptions
            {
                Name = "DelegateToAgent",
                Description = "Delegate a question to a named specialist agent over the A2A protocol and " +
                    $"return its answer. Available agents: {summary}.",
            });
    }

    /// <inheritdoc />
    public async Task<AgentDelegationResult> InvokeAsync(string agentName, string question,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(agentName) || !_clients.TryGetValue(agentName.Trim(), out var client))
        {
            var available = _clients.Count == 0 ? "none" : string.Join(", ", _clients.Keys);
            return new(false, $"Unknown agent '{agentName}'. Available agents: {available}.");
        }

        if (string.IsNullOrWhiteSpace(question))
        {
            return new(false, "No question was provided to delegate.");
        }

        try
        {
            var response = await client.SendMessageAsync(question, Role.User, cancellationToken: cancellationToken);
            if (response.PayloadCase == SendMessageResponseCase.Message && response.Message is { } message)
            {
                var text = string.Concat(message.Parts
                    .Where(p => p.ContentCase == PartContentCase.Text)
                    .Select(p => p.Text));
                return string.IsNullOrWhiteSpace(text)
                    ? new(false, "The agent returned an empty reply.") : new(true, text);
            }

            return new(false, "The agent did not return a direct answer.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "A2A delegation call to agent '{Agent}' failed.", agentName);
            return new(false, $"Could not reach agent '{agentName}': {ex.Message}");
        }
    }

    /// <inheritdoc />
    public void Dispose() => _http.Dispose();
}

/// <summary>A reachable A2A agent's name and description.</summary>
public sealed record A2AAgentInfo(string Name, string Description);

/// <summary>The A2A server's discovery response listing the agents it hosts.</summary>
internal sealed record A2ADiscoveryResponse(IReadOnlyList<A2ADiscoveredAgent> Agents);

/// <summary>A discovered A2A agent's name, path and description.</summary>
internal sealed record A2ADiscoveredAgent(string Name, string Path, string Description);
