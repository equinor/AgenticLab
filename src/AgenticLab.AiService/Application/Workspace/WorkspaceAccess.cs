namespace AgenticLab.AiService.Application.Workspace;

/// <summary>How the service decides which folder, if any, a run may use as its workspace.</summary>
public enum WorkspaceMode
{
    /// <summary>The caller chooses any existing server folder; for trusted local use only.</summary>
    Local,

    /// <summary>No workspace can be opened: no workspace agents, skills, instructions or folder browsing.</summary>
    Disabled,

    /// <summary>
    /// Every run uses the bundled sample workspace, ignoring caller-supplied paths, and only read-only tools
    /// are offered. Safe for shared deployments because nobody can choose a folder or change a file.
    /// </summary>
    ReadOnlySample,
}

/// <summary>
/// The server-wide workspace policy, read from <c>Workspace:Mode</c> (<see cref="WorkspaceMode"/>, default
/// <see cref="WorkspaceMode.Local"/>). The older <c>Workspace:Enabled=false</c> still selects
/// <see cref="WorkspaceMode.Disabled"/>. Every endpoint opens workspaces through <see cref="TryBegin"/>, so the
/// mode is enforced in one place: callers can't choose server folders on shared deployments (see SECURITY.md).
/// </summary>
public sealed class WorkspaceAccess
{
    /// <summary>The configuration key selecting the <see cref="WorkspaceMode"/>.</summary>
    public const string ModeKey = "Workspace:Mode";

    /// <summary>The older on/off key; <c>false</c> selects <see cref="WorkspaceMode.Disabled"/> when no mode is set.</summary>
    public const string EnabledKey = "Workspace:Enabled";

    /// <summary>The configuration key overriding the folder used by <see cref="WorkspaceMode.ReadOnlySample"/>.</summary>
    public const string SampleRootKey = "Workspace:SampleRoot";

    /// <summary>
    /// The tools that can only read: offered in <see cref="WorkspaceMode.ReadOnlySample"/>, where every other
    /// workspace tool (writing, deleting, running commands, fetching the web) is withheld.
    /// </summary>
    public static readonly IReadOnlySet<string> ReadOnlyToolNames =
        new HashSet<string>(["ReadFile", "ListFiles", "ReadSkill", "AskQuestion"], StringComparer.OrdinalIgnoreCase);

    /// <summary>Creates the policy; <paramref name="sampleRoot"/> is only used by <see cref="WorkspaceMode.ReadOnlySample"/>.</summary>
    public WorkspaceAccess(WorkspaceMode mode, string? sampleRoot = null)
    {
        Mode = mode;
        SampleRoot = Path.GetFullPath(sampleRoot ?? Path.Combine(AppContext.BaseDirectory, "sample-workspace"));
    }

    /// <summary>The active mode.</summary>
    public WorkspaceMode Mode { get; }

    /// <summary>The bundled sample folder every run uses in <see cref="WorkspaceMode.ReadOnlySample"/>.</summary>
    public string SampleRoot { get; }

    /// <summary>Whether any workspace can be opened at all.</summary>
    public bool Enabled => Mode != WorkspaceMode.Disabled;

    /// <summary>Whether the caller must supply the workspace path (only in <see cref="WorkspaceMode.Local"/>).</summary>
    public bool RequiresClientPath => Mode == WorkspaceMode.Local;

    /// <summary>Whether only <see cref="ReadOnlyToolNames"/> may be offered.</summary>
    public bool ReadOnly => Mode == WorkspaceMode.ReadOnlySample;

    /// <summary>
    /// Reads <see cref="ModeKey"/>, falling back to <see cref="EnabledKey"/> and then <see cref="WorkspaceMode.Local"/>,
    /// so local use is unchanged. An unknown mode value fails startup rather than silently opening workspaces.
    /// </summary>
    public static WorkspaceAccess FromConfiguration(IConfiguration configuration)
    {
        var configured = configuration[ModeKey];
        WorkspaceMode mode;
        if (!string.IsNullOrWhiteSpace(configured))
        {
            if (!Enum.TryParse(configured.Trim(), ignoreCase: true, out mode) || !Enum.IsDefined(mode))
            {
                throw new InvalidOperationException(
                    $"Unknown {ModeKey} '{configured}'. Use {string.Join(", ", Enum.GetNames<WorkspaceMode>())}.");
            }
        }
        else
        {
            mode = configuration.GetValue(EnabledKey, true) ? WorkspaceMode.Local : WorkspaceMode.Disabled;
        }

        return new WorkspaceAccess(mode, configuration[SampleRootKey]);
    }

    /// <summary>
    /// Opens the run's workspace according to the mode: the caller's <paramref name="root"/> in
    /// <see cref="WorkspaceMode.Local"/>, always the <see cref="SampleRoot"/> in
    /// <see cref="WorkspaceMode.ReadOnlySample"/> (ignoring <paramref name="root"/>), and never when disabled.
    /// Returns null when no usable workspace results.
    /// </summary>
    public WorkspaceScope? TryBegin(string? root) => Mode switch
    {
        WorkspaceMode.Local => WorkspaceScope.TryBegin(root),
        WorkspaceMode.ReadOnlySample => WorkspaceScope.TryBegin(SampleRoot),
        _ => null,
    };

    /// <summary>Whether a tool with this function name may be offered under the active mode.</summary>
    public bool AllowsTool(string toolName) => !ReadOnly || ReadOnlyToolNames.Contains(toolName);
}
