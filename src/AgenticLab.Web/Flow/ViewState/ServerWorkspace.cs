namespace AgenticLab.Web.Flow;

/// <summary>
/// How the AI service handles workspaces, fetched once from <c>GET /workspace</c>. In the read-only sample
/// mode the server decides the folder, so the page hides the workspace path input and treats every
/// workspace agent as ready to run. Until the fetch succeeds (or when it fails), the page keeps the local
/// behavior of asking for a path; the server enforces its mode either way.
/// </summary>
internal sealed class ServerWorkspace(Action notify)
{
    /// <summary>The server's mode: <c>local</c>, <c>sample</c> or <c>disabled</c>.</summary>
    public string Mode { get; private set; } = "local";

    /// <summary>The bundled sample's folder name in the sample mode; null otherwise.</summary>
    public string? SampleName { get; private set; }

    /// <summary>Whether the server supplies a read-only sample workspace instead of asking for a path.</summary>
    public bool IsSample => Mode == "sample";

    /// <summary>Applies the server's answer; a null answer keeps the local defaults.</summary>
    public void Set(WorkspaceInfo? info)
    {
        if (info is null)
        {
            return;
        }

        Mode = info.Mode;
        SampleName = info.SampleName;
        notify();
    }
}
