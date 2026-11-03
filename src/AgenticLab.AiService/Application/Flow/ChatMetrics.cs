using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AgenticLab.AiService.Application.Flow;

/// <summary>
/// Counts chat messages that reach an agent and measures each completed run per model, so models can be
/// compared side by side. Tags are limited to catalogue, host and configured model keys, never user,
/// conversation or message data, because shared dashboards (such as Radix Grafana) can read them.
/// Rejected requests (empty, too long, unknown agent or model) are not counted.
/// </summary>
public static class ChatMetrics
{
    /// <summary>The meter name to subscribe to in OpenTelemetry.</summary>
    public const string MeterName = "AgenticLab.AiService.Chat";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Messages = Meter.CreateCounter<long>(
        "agenticlab.chat.messages", unit: "{message}", description: "Chat messages accepted and run by an agent.");

    private static readonly Histogram<double> Duration = Meter.CreateHistogram<double>(
        "agenticlab.chat.duration", unit: "s", description: "Completed agent run time, including tools and stepping pauses.");

    private static readonly Counter<long> Tokens = Meter.CreateCounter<long>(
        "agenticlab.chat.tokens", unit: "{token}", description: "Tokens used by completed agent runs.");

    private static readonly Histogram<int> ModelCalls = Meter.CreateHistogram<int>(
        "agenticlab.chat.model_calls", unit: "{call}", description: "LLM round-trips per completed agent run.");

    private static readonly Histogram<int> ToolCalls = Meter.CreateHistogram<int>(
        "agenticlab.chat.tool_calls", unit: "{call}", description: "Tool calls requested per completed agent run.");

    /// <summary>Records one accepted message for <paramref name="agent"/> on the given host key.</summary>
    /// <param name="agent">The resolved agent name, not the requested one.</param>
    /// <param name="host">A registered host key from <see cref="VendorHarnessCatalog.HostKey"/>.</param>
    public static void RecordMessage(string agent, string host) =>
        Messages.Add(1, new KeyValuePair<string, object?>("agent", agent), new KeyValuePair<string, object?>("host", host));

    /// <summary>
    /// Records a completed run's duration, tokens, model calls and tool calls by agent, host and model, and
    /// tags <paramref name="activity"/> (the request span) with the same figures.
    /// </summary>
    /// <param name="activity">The request's activity, captured before any streaming yield; may be null.</param>
    /// <param name="agent">The resolved agent name.</param>
    /// <param name="host">A registered host key.</param>
    /// <param name="run">The run's summary; its model is an allow-listed, configured or scripted label.</param>
    public static void RecordRun(Activity? activity, string agent, string host, ModelRunSummary run)
    {
        var model = run.Model ?? "none";
        var tags = new TagList { { "agent", agent }, { "host", host }, { "model", model } };
        Duration.Record(run.DurationMs / 1000d, tags);
        ModelCalls.Record(run.ModelCalls, tags);
        ToolCalls.Record(run.ToolCalls, tags);
        Tokens.Add(run.InputTokens, new TagList { { "agent", agent }, { "host", host }, { "model", model }, { "token.type", "input" } });
        Tokens.Add(run.OutputTokens, new TagList { { "agent", agent }, { "host", host }, { "model", model }, { "token.type", "output" } });

        activity?.SetTag("agenticlab.agent", agent)
            .SetTag("gen_ai.request.model", model)
            .SetTag("gen_ai.usage.input_tokens", run.InputTokens)
            .SetTag("gen_ai.usage.output_tokens", run.OutputTokens)
            .SetTag("agenticlab.model_calls", run.ModelCalls)
            .SetTag("agenticlab.tool_calls", run.ToolCalls)
            .SetTag("agenticlab.duration_ms", run.DurationMs);
    }
}
