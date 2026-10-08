namespace AgenticLab.Web.Flow;

/// <summary>
/// The service's input limits, fetched once from <c>GET /chat/limits</c>, so the composer can stop at the
/// limit and show how many characters are left. Without an answer (an older service or a failed call) no
/// limit is applied here; the service enforces its own limit either way.
/// </summary>
internal sealed class ChatLimits(Action notify)
{
    /// <summary>The longest message or answer the service accepts, in characters; null when unlimited.</summary>
    public int? MaxMessageLength { get; private set; }

    /// <summary>Whether <paramref name="text"/> is longer than the limit.</summary>
    public bool IsTooLong(string? text) => MaxMessageLength is { } max && (text?.Length ?? 0) > max;

    /// <summary>Characters left before the limit, or null when unlimited.</summary>
    public int? Remaining(string? text) => MaxMessageLength is { } max ? max - (text?.Length ?? 0) : null;

    /// <summary>Applies the service's answer; 0 or less means unlimited, and a null answer changes nothing.</summary>
    public void Set(ChatLimitsInfo? info)
    {
        if (info is null)
        {
            return;
        }

        MaxMessageLength = info.MaxMessageLength > 0 ? info.MaxMessageLength : null;
        notify();
    }
}
