namespace AgenticLab.AiService.Application.Skills;

/// <summary>
/// A skill discovered in the active workspace: a small, named bundle of instructions the coding agent
/// can load on demand. Skills follow a progressive-disclosure model — only the <see cref="Name"/> and
/// <see cref="Description"/> are surfaced to the model up front (injected into its context each run),
/// and the full instructions in the skill file are read only when the agent decides to use the skill.
/// </summary>
/// <param name="Name">The skill's unique name, taken from the <c>name</c> frontmatter field.</param>
/// <param name="Description">A short summary of when to use the skill, from the <c>description</c> field.</param>
/// <param name="RelativePath">The workspace-relative path to the skill's <c>SKILL.md</c> file, e.g. <c>skills/get-date/SKILL.md</c>.</param>
/// <param name="AllowedTools">
/// The tools the skill needs, from the optional <c>allowed-tools</c> frontmatter field (comma- or
/// space-separated); empty when not declared. Used to hide skills the workspace mode can't support.
/// </param>
internal sealed record SkillDefinition(string Name, string Description, string RelativePath, IReadOnlyList<string>? AllowedTools = null)
{
    /// <summary>The declared tools, or an empty list when the skill declares none.</summary>
    public IReadOnlyList<string> AllowedTools { get; init; } = AllowedTools ?? [];
}
