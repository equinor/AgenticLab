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
    private long _modelTicks;
    private string? _model;

    /// <summary>Adds one completed model round-trip.</summary>
    /// <param name="model">The model or deployment that answered.</param>
    /// <param name="usage">The provider-reported token usage, when available.</param>
    /// <param name="toolCalls">The function calls the model requested in this round-trip.</param>
    /// <param name="elapsed">Time spent waiting for the model, excluding stepping and breakpoint pauses.</param>
    public void Record(string model, UsageDetails? usage, int toolCalls, TimeSpan elapsed = default)
    {
        Interlocked.Add(ref _inputTokens, usage?.InputTokenCount ?? 0);
        Interlocked.Add(ref _outputTokens, usage?.OutputTokenCount ?? 0);
        Interlocked.Increment(ref _modelCalls);
        Interlocked.Add(ref _toolCalls, toolCalls);
        Interlocked.Add(ref _modelTicks, elapsed.Ticks);
        Volatile.Write(ref _model, model);
    }

    /// <summary>Snapshots the totals for a run that took <paramref name="duration"/>.</summary>
    public ModelRunSummary Summarize(TimeSpan duration) => new(
        Volatile.Read(ref _model),
        (long)duration.TotalMilliseconds,
        Interlocked.Read(ref _inputTokens),
        Interlocked.Read(ref _outputTokens),
        Volatile.Read(ref _modelCalls),
        Volatile.Read(ref _toolCalls),
        (long)TimeSpan.FromTicks(Interlocked.Read(ref _modelTicks)).TotalMilliseconds);
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
/// <param name="ModelDurationMs">Time spent waiting for the local model across round-trips, excluding tools, stepping and breakpoint pauses.</param>
public sealed record ModelRunSummary(string? Model, long DurationMs, long InputTokens, long OutputTokens, int ModelCalls, int ToolCalls, long ModelDurationMs = 0);

/// <summary>
/// The provider-reported tokens of a single model round-trip. Unlike <see cref="ModelRunSummary"/>, which sums
/// every round-trip of a run, this describes one request and its response.
/// </summary>
/// <param name="InputTokens">Tokens in the whole request, including the tool catalogue and message framing.</param>
/// <param name="OutputTokens">Tokens the model generated in response.</param>
/// <param name="CachedInputTokens">
/// The part of <paramref name="InputTokens"/> the provider read from its prompt cache; null when it does not
/// report cache usage, which is different from a reported zero (nothing was served from cache).
/// </param>
public sealed record ModelCallUsage(long InputTokens, long OutputTokens, long? CachedInputTokens = null)
{
    /// <summary>
    /// Maps provider usage to a round-trip's tokens. Null unless the provider reported both counts, so a
    /// reported zero stays distinct from missing or partial usage.
    /// </summary>
    public static ModelCallUsage? From(UsageDetails? usage) =>
        usage is { InputTokenCount: { } input, OutputTokenCount: { } output }
            ? new(input, output, usage.CachedInputTokenCount)
            : null;
}
