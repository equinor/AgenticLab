using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace AgenticLab.Examples.Security.Scripting;

/// <summary>
/// A deterministic, offline <see cref="IChatClient"/> that plays a <see cref="Playbook"/> instead of calling a
/// model. It behaves like a model that has already been manipulated: it requests the scripted tools in order and
/// then answers. Only tools present in the request's tool list are requested, so per-run tool toggles stop a step
/// exactly as they would for a real model, and the host's function invocation, breakpoints and capture pipeline
/// wrap it unchanged. Nothing here contacts a network or a model provider.
/// </summary>
public sealed class ScenarioModel(Playbook playbook) : IChatClient
{
    /// <summary>The model identifier reported in captures and the agent catalogue.</summary>
    public const string ModelId = "scripted-scenario";

    private readonly Playbook _playbook = playbook.Validated();

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var (contents, finish) = Next(messages, options);
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, contents))
        {
            FinishReason = finish,
            ModelId = ModelId,
            ResponseId = Guid.NewGuid().ToString("N"),
        };
        return Task.FromResult(response);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var (contents, finish) = Next(messages, options);
        var responseId = Guid.NewGuid().ToString("N");
        var messageId = Guid.NewGuid().ToString("N");

        if (contents is [TextContent text])
        {
            var chunks = Chunk(text.Text);
            for (var i = 0; i < chunks.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                yield return new ChatResponseUpdate(ChatRole.Assistant, chunks[i])
                {
                    ModelId = ModelId,
                    ResponseId = responseId,
                    MessageId = messageId,
                    FinishReason = i == chunks.Count - 1 ? finish : null,
                };
                await Task.Yield();
            }
            yield break;
        }

        yield return new ChatResponseUpdate(ChatRole.Assistant, contents)
        {
            ModelId = ModelId,
            ResponseId = responseId,
            MessageId = messageId,
            FinishReason = finish,
        };
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : null;

    /// <inheritdoc />
    public void Dispose() { }

    private (IList<AIContent> Contents, ChatFinishReason Finish) Next(IEnumerable<ChatMessage> messages, ChatOptions? options)
    {
        var history = messages.ToList();
        var lastUser = history.FindLastIndex(message => message.Role == ChatRole.User);
        var earlierTurnAnswered = history.Take(Math.Max(lastUser, 0))
            .Any(message => message.Role == ChatRole.Assistant && message.Contents.OfType<TextContent>().Any());
        if (earlierTurnAnswered) return Text(_playbook.FollowUp);

        var results = history.Skip(lastUser + 1)
            .SelectMany(message => message.Contents.OfType<FunctionResultContent>())
            .Select(result => Stringify(result.Result))
            .ToList();

        var step = results.Count < _playbook.Steps.Count ? _playbook.Steps[results.Count] : _playbook.Steps[^1];
        switch (step)
        {
            case ScriptedToolCall call when options?.Tools?.Any(tool => tool.Name == call.Tool) == true:
                var arguments = new Dictionary<string, object?>(call.Arguments);
                return ([new FunctionCallContent(Guid.NewGuid().ToString("N"), call.Tool, arguments)], ChatFinishReason.ToolCalls);
            case ScriptedToolCall call:
                return Text($"I wanted to call {call.Tool} next, but that tool is not available in this run, so I stopped there. " +
                            "The step that would have followed cannot happen without it.");
            case ScriptedAnswer answer:
                return Text(answer.Compose(results));
            default:
                throw new InvalidOperationException($"Unknown scripted step {step.GetType().Name}.");
        }
    }

    private static (IList<AIContent>, ChatFinishReason) Text(string text) => ([new TextContent(text)], ChatFinishReason.Stop);

    private static string Stringify(object? result) => result switch
    {
        null => string.Empty,
        string text => text,
        JsonElement element => element.ValueKind == JsonValueKind.String ? element.GetString() ?? string.Empty : element.ToString(),
        _ => result.ToString() ?? string.Empty,
    };

    private static List<string> Chunk(string text)
    {
        const int size = 24;
        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += size) chunks.Add(text.Substring(i, Math.Min(size, text.Length - i)));
        if (chunks.Count == 0) chunks.Add(string.Empty);
        return chunks;
    }
}
