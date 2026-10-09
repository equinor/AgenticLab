namespace AgenticLab.AiService.Application.Conversations;

/// <summary>
/// Limits on what users may send, bound from the <c>Chat</c> configuration section. Enforced by the chat
/// endpoints for messages and for answers to an agent's questions, and published over <c>GET /chat/limits</c>
/// so clients can apply the same limit while typing. The server check is the authoritative one.
/// </summary>
public sealed class ChatInputOptions
{
    /// <summary>The configuration section the options are bound from.</summary>
    public const string SectionName = "Chat";

    /// <summary>The longest message or answer accepted, in characters; 0 or less means no limit (the default).</summary>
    public int MaxMessageLength { get; set; }

    /// <summary>Whether <paramref name="text"/> is within the limit; null text counts as empty.</summary>
    public bool Allows(string? text) => MaxMessageLength <= 0 || (text?.Length ?? 0) <= MaxMessageLength;

    /// <summary>The error a client gets for text over the limit.</summary>
    public string TooLongMessage => $"Messages can be at most {MaxMessageLength} characters.";
}
