using System.Diagnostics.Metrics;

namespace AgenticLab.AiService.Application.Flow;

/// <summary>
/// Counts chat messages that reach an agent, by agent and host. Tags are limited to catalogue and host keys,
/// never user, conversation or message data, because shared dashboards (such as Radix Grafana) can read them.
/// Rejected requests (empty, too long, unknown agent) are not counted.
/// </summary>
public static class ChatMetrics
{
    /// <summary>The meter name to subscribe to in OpenTelemetry.</summary>
    public const string MeterName = "AgenticLab.AiService.Chat";

    private static readonly Meter Meter = new(MeterName);

    private static readonly Counter<long> Messages = Meter.CreateCounter<long>(
        "agenticlab.chat.messages", unit: "{message}", description: "Chat messages accepted and run by an agent.");

    /// <summary>Records one accepted message for <paramref name="agent"/> on the given host key.</summary>
    /// <param name="agent">The resolved agent name, not the requested one.</param>
    /// <param name="host">A registered host key from <see cref="VendorHarnessCatalog.HostKey"/>.</param>
    public static void RecordMessage(string agent, string host) =>
        Messages.Add(1, new KeyValuePair<string, object?>("agent", agent), new KeyValuePair<string, object?>("host", host));
}
