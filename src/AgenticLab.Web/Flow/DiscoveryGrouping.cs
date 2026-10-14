namespace AgenticLab.Web.Flow;

/// <summary>Discovered items the selected agent can use, plus the rest grouped by the example claiming them.</summary>
/// <param name="Scoped">Whether an agent context was supplied, so <paramref name="Available"/> is meaningful.</param>
/// <param name="Available">Items in the agent's relevant set, in discovery order; empty when unscoped.</param>
/// <param name="Groups">The remaining items grouped by owner — shared first, then examples by title.</param>
internal sealed record DiscoveryGroupView(bool Scoped, IReadOnlyList<DiscoveryItemDto> Available, IReadOnlyList<DiscoveryGroup> Groups);

/// <summary>One owner's discovered items; <paramref name="ExampleId"/> is null for the shared group.</summary>
internal sealed record DiscoveryGroup(string Title, string? ExampleId, IReadOnlyList<DiscoveryItemDto> Items);

/// <summary>
/// Pure grouping for the Discovery view. Rediscovery itself stays process-wide; this only decides how one
/// source's items are presented for the selected agent (inside Flow) or for everyone (standalone page).
/// </summary>
internal static class DiscoveryGrouping
{
    /// <summary>The title of the group holding items no enabled example claims.</summary>
    public const string SharedTitle = "Shared";

    /// <summary>
    /// Splits <paramref name="items"/> into the agent's available set and owner groups. A null or empty
    /// <paramref name="relevant"/> means no agent scope: every item lands in its owner group.
    /// </summary>
    /// <param name="items">The discovered items for one source.</param>
    /// <param name="relevant">The names the selected agent holds (its bounded remote agents and tool names), or null.</param>
    /// <param name="exampleTitle">Maps an example id to its display name; a null result falls back to the id.</param>
    public static DiscoveryGroupView Group(IReadOnlyList<DiscoveryItemDto> items, IReadOnlyCollection<string>? relevant,
        Func<string, string?> exampleTitle)
    {
        var scoped = relevant is { Count: > 0 };
        var names = scoped ? new HashSet<string>(relevant!, StringComparer.OrdinalIgnoreCase) : null;
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
        return new(scoped, available, groups);
    }
}
