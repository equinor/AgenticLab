using Microsoft.Extensions.AI;

namespace AgenticLab.ModelProviders;

/// <summary>
/// Sends each request to the client for the currently selected model, or to <paramref name="defaultModel"/>
/// when nothing is selected. Lets a host keep one long-lived agent pipeline while the model changes per
/// request; the selector is read on every call, so it must return an already-validated model.
/// </summary>
/// <param name="defaultModel">The model used when <paramref name="selected"/> returns null.</param>
/// <param name="selected">Returns the model selected for the current request, or null.</param>
/// <param name="clientFor">Returns the (cached) client for a model; the caller owns its lifetime.</param>
public sealed class ModelRoutingChatClient(string defaultModel, Func<string?> selected, Func<string, IChatClient> clientFor) : IChatClient
{
    /// <summary>The model the next request will use.</summary>
    public string CurrentModel => selected() ?? defaultModel;

    /// <inheritdoc />
    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        clientFor(CurrentModel).GetResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc />
    public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
        clientFor(CurrentModel).GetStreamingResponseAsync(messages, options, cancellationToken);

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is null && serviceType.IsInstanceOfType(this) ? this : clientFor(CurrentModel).GetService(serviceType, serviceKey);

    /// <summary>Does nothing: the routed clients are owned by the caller's cache.</summary>
    public void Dispose() { }
}

/// <summary>The HTTP header that carries a selected model from AiService to A2A delegations.</summary>
public static class ModelSelectionHeader
{
    /// <summary>The header name; its value must be on the receiving host's <see cref="ModelChoices"/>.</summary>
    public const string Name = "X-AgenticLab-Model";
}
