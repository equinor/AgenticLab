using System.Globalization;

namespace AgenticLab.Web.Flow;

/// <summary>Formats a run's figures for the conversation, so answers from different models can be compared at a glance.</summary>
internal static class RunSummaryText
{
    /// <summary>A compact line such as <c>1.2 s · 812 tokens · 2 tool calls</c>; tokens are omitted when the provider reported none.</summary>
    public static string Describe(RunSummary run)
    {
        var parts = new List<string> { string.Create(CultureInfo.InvariantCulture, $"{run.DurationMs / 1000d:0.0} s") };
        var tokens = run.InputTokens + run.OutputTokens;
        if (tokens > 0) parts.Add(string.Create(CultureInfo.InvariantCulture, $"{tokens:N0} tokens"));
        parts.Add($"{run.ToolCalls} tool call{(run.ToolCalls == 1 ? "" : "s")}");
        return string.Join(" · ", parts);
    }

    /// <summary>The full breakdown for a tooltip.</summary>
    public static string Detail(RunSummary run) => string.Create(CultureInfo.InvariantCulture,
        $"Model: {run.Model ?? "none"}\nDuration: {run.DurationMs:N0} ms\nInput tokens: {run.InputTokens:N0}\n" +
        $"Output tokens: {run.OutputTokens:N0}\nLLM round-trips: {run.ModelCalls}\nTool calls: {run.ToolCalls}");
}
