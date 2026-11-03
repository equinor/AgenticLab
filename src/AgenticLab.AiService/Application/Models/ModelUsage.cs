using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Models;

/// <summary>
/// Thread-safe totals for one agent run's model calls. Counts only the local agent's calls: A2A specialists
/// run on another host and report their own telemetry.
/// </summary>
public sealed class ModelUsage
{
    private long _inputTokens;
    private long _outputTokens;
    private int _modelCalls;
    private int _toolCalls;
    private string? _model;

    /// <summary>Adds one completed model round-trip.</summary>
    /// <param name="model">The model or deployment that answered.</param>
    /// <param name="usage">The provider-reported token usage, when available.</param>
    /// <param name="toolCalls">The function calls the model requested in this round-trip.</param>
    public void Record(string model, UsageDetails? usage, int toolCalls)
    {
        Interlocked.Add(ref _inputTokens, usage?.InputTokenCount ?? 0);
        Interlocked.Add(ref _outputTokens, usage?.OutputTokenCount ?? 0);
        Interlocked.Increment(ref _modelCalls);
        Interlocked.Add(ref _toolCalls, toolCalls);
        Volatile.Write(ref _model, model);
    }

    /// <summary>Snapshots the totals for a run that took <paramref name="duration"/>.</summary>
    public ModelRunSummary Summarize(TimeSpan duration) => new(
        Volatile.Read(ref _model),
        (long)duration.TotalMilliseconds,
        Interlocked.Read(ref _inputTokens),
        Interlocked.Read(ref _outputTokens),
        Volatile.Read(ref _modelCalls),
        Volatile.Read(ref _toolCalls));
}

/// <summary>
/// The model that answered a run and what it cost, returned with each answer so models can be compared.
/// Token counts are zero when the provider reports no usage.
/// </summary>
/// <param name="Model">The model or deployment that answered; null when no model call completed.</param>
/// <param name="DurationMs">Wall-clock run time, including tool execution and any stepping pauses.</param>
/// <param name="InputTokens">Prompt tokens across all round-trips.</param>
/// <param name="OutputTokens">Completion tokens across all round-trips.</param>
/// <param name="ModelCalls">LLM round-trips.</param>
/// <param name="ToolCalls">Tool calls the model requested.</param>
public sealed record ModelRunSummary(string? Model, long DurationMs, long InputTokens, long OutputTokens, int ModelCalls, int ToolCalls);
