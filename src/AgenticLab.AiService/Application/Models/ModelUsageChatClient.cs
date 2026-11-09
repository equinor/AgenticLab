using System.Diagnostics;
using System.Runtime.CompilerServices;
using AgenticLab.ModelProviders;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Models;

/// <summary>
/// Records each round-trip of one model into the active <see cref="ModelSelectionScope"/>'s usage.
/// A transparent pass-through outside an agent run.
/// </summary>
/// <param name="inner">The model's client.</param>
/// <param name="model">The model or deployment label recorded for this client.</param>
public sealed class ModelUsageChatClient(IChatClient inner, string model) : DelegatingChatClient(inner)
{
    /// <inheritdoc />
    public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await base.GetResponseAsync(messages, options, cancellationToken);
        ModelSelectionScope.Current?.Usage.Record(model, response.Usage,
            response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>().Count(), stopwatch.Elapsed);
        return response;
    }

    /// <inheritdoc />
    public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Captured before the first yield, which resets AsyncLocal values in this iterator.
        var usage = ModelSelectionScope.Current?.Usage;
        UsageDetails? details = null;
        var toolCalls = 0;
        var stopwatch = Stopwatch.StartNew();
        await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            foreach (var content in update.Contents)
            {
                switch (content)
                {
                    case UsageContent reported:
                        (details ??= new()).Add(reported.Details);
                        break;
                    case FunctionCallContent:
                        toolCalls++;
                        break;
                }
            }

            // Stepping and breakpoints suspend the consumer at this yield; that wait is not model time.
            stopwatch.Stop();
            yield return update;
            stopwatch.Start();
        }

        usage?.Record(model, details, toolCalls, stopwatch.Elapsed);
    }
}

/// <summary>Forwards the run's selected model to A2A servers so delegations answer on the same model.</summary>
public sealed class ModelSelectionHeaderHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ModelSelectionScope.Current?.Model is { } model)
        {
            request.Headers.Remove(ModelSelectionHeader.Name);
            request.Headers.TryAddWithoutValidation(ModelSelectionHeader.Name, model);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
