namespace AgenticLab.Web.Flow;

/// <summary>Discovered items the selected agent can use, plus the rest grouped by the example claiming them.</summary>
/// <param name="Scoped">Whether an agent context was supplied, so <paramref name="Available"/> is meaningful.</param>
/// <param name="Available">Items in the agent's relevant set, in discovery order; empty when unscoped.</param>
/// <param name="Groups">The remaining items grouped by owner — shared first, then examples by title.</param>
internal sealed record DiscoveryGroupView(bool Scoped, IReadOnlyList<DiscoveryItemDto> Available, IReadOnlyList<DiscoveryGroup> Groups);

/// <summary>One owner's discovered items; <paramref name="ExampleId"/> is null for the shared group.</summary>
internal sealed record DiscoveryGroup(string Title, string? ExampleId, IReadOnlyList<DiscoveryItemDto> Items);

/// <summary>
/// The selected agent's protocol capabilities, kept per source so an MCP tool name never counts as an
/// A2A agent (or vice versa). Empty sets for an agent without MCP/A2A support mean no scope at all.
/// </summary>
/// <param name="McpToolNames">The MCP tool names the agent holds (exact-name, as the backend selects them).</param>
/// <param name="RemoteAgentNames">The remote A2A agents the service bounded the agent to.</param>
public sealed record DiscoveryScope(IReadOnlyCollection<string> McpToolNames, IReadOnlyCollection<string> RemoteAgentNames)
{
    /// <summary>Whether the agent can use either protocol; otherwise every group renders expanded.</summary>
    public bool IsScoped => McpToolNames.Count > 0 || RemoteAgentNames.Count > 0;

    /// <summary>The relevant set for a discovery source, or null when the agent is not protocol-capable.</summary>
    public IReadOnlyCollection<string>? For(string source) => !IsScoped ? null
        : source == "mcp" ? McpToolNames
        : source == "a2a" ? RemoteAgentNames
        : [];
}

/// <summary>
/// Pure grouping for the Discovery view. Rediscovery itself stays process-wide; this only decides how one
/// source's items are presented for the selected agent (inside Flow) or for everyone (standalone page).
/// </summary>
internal static class DiscoveryGrouping
{
    /// <summary>The title of the group holding items no enabled example claims.</summary>
    public const string SharedTitle = "Shared";

    /// <summary>MCP tools are selected by exact name on the backend; A2A agents are resolved case-insensitively.</summary>
    public static StringComparer ComparerFor(string source) =>
        source == "mcp" ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    /// <summary>
    /// Splits <paramref name="items"/> into the agent's available set and owner groups. A null
    /// <paramref name="relevant"/> means no agent scope: every item lands in its owner group. An empty
    /// set is scoped with nothing available.
    /// </summary>
    /// <param name="items">The discovered items for one source.</param>
    /// <param name="relevant">The names the selected agent holds for this source, or null for no scope.</param>
    /// <param name="exampleTitle">Maps an example id to its display name; a null result falls back to the id.</param>
    /// <param name="comparer">How names match for this source; see <see cref="ComparerFor"/>.</param>
    public static DiscoveryGroupView Group(IReadOnlyList<DiscoveryItemDto> items, IReadOnlyCollection<string>? relevant,
        Func<string, string?> exampleTitle, StringComparer comparer)
    {
        var names = relevant is null ? null : new HashSet<string>(relevant, comparer);
        var available = new List<DiscoveryItemDto>();
        var rest = new List<DiscoveryItemDto>();
        foreach (var item in items)
        {
            (names is not null && names.Contains(item.Name) ? available : rest).Add(item);
        }

        var groups = rest
            .GroupBy(item => item.ExampleId, StringComparer.OrdinalIgnoreCase)
            .Select(group => new DiscoveryGroup(
                group.Key is null ? SharedTitle : exampleTitle(group.Key) ?? group.Key,
                group.Key,
                group.ToArray()))
            .OrderBy(group => group.ExampleId is null ? 0 : 1)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return new(names is not null, available, groups);
    }
}
