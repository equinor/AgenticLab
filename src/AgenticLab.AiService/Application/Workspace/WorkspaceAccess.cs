namespace AgenticLab.AiService.Application.Workspace;

/// <summary>
/// The server-wide switch for workspace features, read from <c>Workspace:Enabled</c> (default <c>true</c>).
/// When disabled, no request can open a workspace on the server: the workspace agents (Ask, Plan, Coder)
/// are not registered, workspace-defined agents cannot be discovered or run, and the workspace endpoints
/// return empty results. Disable it on shared deployments, where callers must not choose server folders
/// for the file, terminal and web-fetch tools (see SECURITY.md).
/// </summary>
public sealed class WorkspaceAccess(bool enabled)
{
    /// <summary>The configuration key that enables or disables workspace features.</summary>
    public const string EnabledKey = "Workspace:Enabled";

    /// <summary>Whether requests may open a workspace on this server.</summary>
    public bool Enabled { get; } = enabled;

    /// <summary>Reads <see cref="EnabledKey"/>, treating an absent value as enabled so local use is unchanged.</summary>
    public static WorkspaceAccess FromConfiguration(IConfiguration configuration) =>
        new(configuration.GetValue(EnabledKey, true));

    /// <summary>
    /// Like <see cref="WorkspaceScope.TryBegin"/>, but always returns null while workspace features are
    /// disabled, so every endpoint treats the path as unusable regardless of what exists on disk.
    /// </summary>
    public WorkspaceScope? TryBegin(string? root) => Enabled ? WorkspaceScope.TryBegin(root) : null;
}
