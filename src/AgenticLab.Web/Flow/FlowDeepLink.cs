namespace AgenticLab.Web.Flow;

/// <summary>
/// A request to open the flow page with a prepared starting state, carried in the <c>/flow</c> query string.
/// Every member is optional; unknown or unavailable values are ignored when applied. The prompt is only
/// pre-filled in the composer and is never sent automatically.
/// </summary>
internal sealed record FlowLinkRequest(
    string? Host = null,
    string? Agent = null,
    string? Prompt = null,
    DiagramPreset? View = null,
    bool ExpandHarness = false,
    FlowMode? Mode = null,
    IReadOnlyList<string>? Breakpoints = null,
    IReadOnlyList<string>? Tools = null)
{
    /// <summary>Whether the request carries nothing to apply (a plain <c>/flow</c> visit).</summary>
    public bool IsEmpty =>
        Host is null && Agent is null && Prompt is null && View is null && !ExpandHarness && Mode is null
        && Breakpoints is not { Count: > 0 } && Tools is not { Count: > 0 };
}

/// <summary>What a deep link changed, so the page knows whether the URL replaced the stored host/agent.</summary>
internal readonly record struct FlowLinkOutcome(bool HostApplied, bool AgentApplied);

/// <summary>
/// Builds, parses and applies <c>/flow</c> deep links. The query-string vocabulary is: <c>host</c> (catalogue key or
/// legacy alias), <c>agent</c>, <c>prompt</c>, <c>view</c> (<c>basic</c>|<c>technical</c>), <c>harness</c> (<c>1</c> expands the
/// harness anatomy), <c>mode</c> (<c>auto</c>|<c>manual</c>), <c>breakpoints</c> (comma-separated kinds) and <c>tools</c>
/// (comma-separated tool names; a leading <c>-</c> disables the tool). A deep-linked host applies for that page load
/// only and is not saved as the user's preferred host.
/// </summary>
internal static class FlowDeepLink
{
    /// <summary>The flow page route the links point at.</summary>
    public const string Path = "/flow";

    /// <summary>Builds the <c>/flow?...</c> URL for a request; an empty request yields the plain route.</summary>
    public static string Href(FlowLinkRequest request)
    {
        var parts = new List<string>();
        Add(parts, "host", request.Host);
        Add(parts, "agent", request.Agent);
        Add(parts, "view", request.View?.ToString().ToLowerInvariant());
        if (request.ExpandHarness) Add(parts, "harness", "1");
        Add(parts, "mode", request.Mode?.ToString().ToLowerInvariant());
        Add(parts, "breakpoints", Join(request.Breakpoints));
        Add(parts, "tools", Join(request.Tools));
        Add(parts, "prompt", request.Prompt);
        return parts.Count == 0 ? Path : $"{Path}?{string.Join('&', parts)}";

        static void Add(List<string> parts, string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                parts.Add($"{name}={Uri.EscapeDataString(value)}");
            }
        }

        static string? Join(IReadOnlyList<string>? values) =>
            values is { Count: > 0 } ? string.Join(',', values) : null;
    }

    /// <summary>Parses raw query-string values; unrecognised view/mode words become null, blanks are dropped.</summary>
    public static FlowLinkRequest Parse(string? host, string? agent, string? prompt, string? view, string? harness,
        string? mode, string? breakpoints, string? tools) =>
        new(
            Clean(host),
            Clean(agent),
            Clean(prompt),
            Clean(view)?.ToLowerInvariant() switch { "basic" => DiagramPreset.Basic, "technical" => DiagramPreset.Technical, _ => null },
            Clean(harness) is "1" or "true",
            Clean(mode)?.ToLowerInvariant() switch { "auto" => FlowMode.Auto, "manual" => FlowMode.Manual, _ => null },
            Split(breakpoints),
            Split(tools));

    /// <summary>
    /// Applies a request to loaded view state in dependency order: host, then agent (which resets the per-agent
    /// toggles), then diagram, run options and finally the composer text. Call after the vendor and agent
    /// catalogues are loaded. Unknown hosts, agents, breakpoint kinds and tool names are ignored.
    /// </summary>
    public static FlowLinkOutcome Apply(FlowLinkRequest request, FlowViewState view)
    {
        var hostApplied = false;
        if (request.Host is { } host && HostKnown(view.Roster, host))
        {
            view.HostKey = view.Roster.RestoreHost(host);
            view.SelectedAgent = view.Roster.VendorDefaultAgent ?? view.SelectedAgent;
            hostApplied = true;
        }

        var agentApplied = false;
        if (request.Agent is { } agent)
        {
            var choice = view.Roster.AvailableAgents.FirstOrDefault(c => string.Equals(c.Name, agent, StringComparison.OrdinalIgnoreCase));
            if (choice is not null)
            {
                view.SelectedAgent = choice.Name;
                agentApplied = true;
            }
        }

        if (request.View is { } preset)
        {
            view.Diagram.ApplyPreset(preset);
        }

        if (request.ExpandHarness)
        {
            view.Diagram.ExpandHarness = true;
        }

        if (request.Mode is { } mode)
        {
            view.Options.Mode = mode;
        }

        foreach (var kind in request.Breakpoints ?? [])
        {
            var known = RunOptions.BreakpointOptions.FirstOrDefault(o => string.Equals(o.Kind, kind, StringComparison.OrdinalIgnoreCase)).Kind;
            if (known is not null)
            {
                view.Options.SetBreakpoint(known, true);
            }
        }

        foreach (var entry in request.Tools ?? [])
        {
            var disable = entry.StartsWith('-');
            var name = disable ? entry[1..] : entry.TrimStart('+');
            var tool = view.Agent.ToolNames.FirstOrDefault(t => string.Equals(t, name, StringComparison.OrdinalIgnoreCase));
            if (tool is not null)
            {
                view.Options.SetToolEnabled(tool, !disable);
            }
        }

        if (request.Prompt is { } prompt)
        {
            view.Message = view.ChatLimits.MaxMessageLength is { } max && prompt.Length > max ? prompt[..max] : prompt;
        }

        return new FlowLinkOutcome(hostApplied, agentApplied);
    }

    private static bool HostKnown(AgentRoster roster, string host) =>
        roster.AvailableHosts.Any(h => string.Equals(h.Key, host, StringComparison.OrdinalIgnoreCase)
            || roster.PresentationFor(h.Key)?.LegacyKeys.Contains(host, StringComparer.OrdinalIgnoreCase) == true);

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static IReadOnlyList<string>? Split(string? value)
    {
        var items = value?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return items is { Length: > 0 } ? items : null;
    }
}
