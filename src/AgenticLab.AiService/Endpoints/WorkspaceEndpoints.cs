namespace AgenticLab.AiService.Endpoints;

/// <summary>
/// Read-only endpoints that inspect a workspace: the server's workspace mode, the workspace's skills, custom
/// instructions and candidate repo folders. They follow the <see cref="WorkspaceAccess"/> mode: empty results
/// while disabled, and always the bundled sample (ignoring caller paths) in the read-only sample mode.
/// </summary>
internal static class WorkspaceEndpoints
{
    public static IEndpointRouteBuilder MapWorkspaceEndpoints(this IEndpointRouteBuilder app)
    {
        // Tells a client how this server handles workspaces, so it can hide the path input when the server
        // decides the folder. The sample's server path is not exposed, only its folder name.
        app.MapGet("/workspace", (WorkspaceAccess access) => Results.Ok(new WorkspaceInfo(
            access.Mode switch
            {
                WorkspaceMode.Local => "local",
                WorkspaceMode.ReadOnlySample => "sample",
                _ => "disabled",
            },
            access.ReadOnly ? Path.GetFileName(access.SampleRoot) : null,
            access.ReadOnly)));

        // Lists the skills discovered in a given workspace (names + descriptions) so a client can show the
        // skill catalogue before a run starts. Returns an empty list when the path is missing/invalid or the
        // workspace declares no skills.
        app.MapPost("/skills", (SkillsRequest request, SkillLoader skills, WorkspaceAccess access) =>
        {
            using var workspace = access.TryBegin(request.Workspace);
            if (workspace is null)
            {
                return Results.Ok(new SkillsResponse(Array.Empty<SkillInfo>()));
            }

            var discovered = skills.Load()
                .Select(s => new SkillInfo(s.Name, s.Description))
                .ToList();
            return Results.Ok(new SkillsResponse(discovered));
        });

        // Lists the custom instructions discovered in a given workspace (names + descriptions) so a client
        // can offer them per run. Returns an empty list when the path is missing/invalid or there are none.
        app.MapPost("/instructions", (InstructionsRequest request, InstructionLoader instructions, WorkspaceAccess access) =>
        {
            using var workspace = access.TryBegin(request.Workspace);
            if (workspace is null)
            {
                return Results.Ok(new InstructionsResponse(Array.Empty<InstructionInfo>()));
            }

            var discovered = instructions.Load()
                .Select(i => new InstructionInfo(i.Name, i.Description))
                .ToList();
            return Results.Ok(new InstructionsResponse(discovered));
        });

        // Lists the immediate sub-folders of one or more base folders so a client can suggest workspace paths
        // (e.g. the repo folders under a "GitHub" directory the user pointed at). Only in local mode: when
        // the server decides the folder, there is nothing to browse.
        app.MapPost("/workspaces", (WorkspaceBrowseRequest request, WorkspaceAccess access) =>
            Results.Ok(new WorkspaceBrowseResponse(access.RequiresClientPath ? BrowseWorkspaces(request.Bases) : Array.Empty<WorkspaceEntry>())));

        return app;
    }

    // A read-only directory listing that skips blank/non-existent bases and hidden/system sub-folders,
    // dedups by full path, and caps the result so a huge base folder can't flood the client.
    private static IReadOnlyList<WorkspaceEntry> BrowseWorkspaces(IReadOnlyList<string>? bases)
    {
        if (bases is not { Count: > 0 })
        {
            return Array.Empty<WorkspaceEntry>();
        }

        const int maxEntries = 300;
        var entries = new List<WorkspaceEntry>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in bases)
        {
            if (entries.Count >= maxEntries || string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string full;
            try
            {
                full = Path.GetFullPath(raw.Trim());
            }
            catch
            {
                continue;
            }

            if (!Directory.Exists(full))
            {
                continue;
            }

            IEnumerable<string> subdirs;
            try
            {
                subdirs = Directory.EnumerateDirectories(full).OrderBy(d => d, StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // A base we can't read (permissions) simply contributes nothing.
                continue;
            }

            foreach (var dir in subdirs)
            {
                if (entries.Count >= maxEntries)
                {
                    break;
                }

                try
                {
                    var attrs = File.GetAttributes(dir);
                    if (attrs.HasFlag(FileAttributes.Hidden) || attrs.HasFlag(FileAttributes.System))
                    {
                        continue;
                    }
                }
                catch
                {
                    continue;
                }

                if (seen.Add(dir))
                {
                    entries.Add(new WorkspaceEntry(dir, Path.GetFileName(dir), full));
                }
            }
        }

        return entries;
    }
}

internal sealed record WorkspaceInfo(string Mode, string? SampleName, bool ReadOnly);
internal sealed record SkillsRequest(string? Workspace);
internal sealed record SkillsResponse(IReadOnlyList<SkillInfo> Skills);
internal sealed record SkillInfo(string Name, string Description);
internal sealed record InstructionsRequest(string? Workspace);
internal sealed record InstructionsResponse(IReadOnlyList<InstructionInfo> Instructions);
internal sealed record InstructionInfo(string Name, string Description);
internal sealed record WorkspaceBrowseRequest(IReadOnlyList<string>? Bases);
internal sealed record WorkspaceBrowseResponse(IReadOnlyList<WorkspaceEntry> Directories);
internal sealed record WorkspaceEntry(string Path, string Name, string Base);
