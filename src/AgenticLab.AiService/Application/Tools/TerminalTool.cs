using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Tools;

/// <summary>
/// Runs shell commands inside the active <see cref="WorkspaceScope"/> on the coding agent's behalf.
/// Only a fixed allowlist of exact command lines may run: <c>whoami</c> plus the OS's read-only
/// date/time command (<c>date /t</c> and <c>time /t</c> on Windows, <c>date</c> on macOS/Linux). Any other
/// command or extra argument is rejected, so the tool cannot chain, script or change the system.
/// Executables are started by absolute path from the system directories, never resolved through the
/// working directory or the inherited <c>PATH</c>, so a file written into the workspace cannot shadow
/// an allowed command. Only the Windows <c>date</c>/<c>time</c> built-ins go through <c>cmd.exe</c>.
/// Commands use the workspace root as the working directory, output is captured, and a timeout
/// prevents a stuck process from hanging the run.
/// </summary>
public sealed class TerminalTool
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(60);

    private static readonly string[] WindowsCommands = ["whoami", "date /t", "time /t"];
    private static readonly string[] UnixCommands = ["whoami", "date"];
    private static readonly string[] UnixSearchDirectories = ["/usr/bin", "/bin"];

    private readonly bool _isWindows;

    /// <summary>Creates the tool with the allowlist for the operating system the service runs on.</summary>
    public TerminalTool() : this(OperatingSystem.IsWindows())
    {
    }

    internal TerminalTool(bool isWindows) => _isWindows = isWindows;

    /// <summary>The exact command lines permitted on this operating system.</summary>
    public IReadOnlyList<string> AllowedCommands => AllowedCommandsFor(_isWindows);

    /// <summary>The exact command lines permitted on Windows or on macOS/Linux.</summary>
    internal static IReadOnlyList<string> AllowedCommandsFor(bool isWindows) => isWindows ? WindowsCommands : UnixCommands;

    /// <summary>
    /// Returns the allowlisted command line matching <paramref name="command"/> and
    /// <paramref name="arguments"/> after trimming and collapsing whitespace, or <c>null</c> when none matches.
    /// </summary>
    internal static string? Match(string? command, string? arguments, bool isWindows)
    {
        var parts = $"{command} {arguments}".Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var commandLine = string.Join(' ', parts);
        var comparison = isWindows ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return AllowedCommandsFor(isWindows).FirstOrDefault(allowed => allowed.Equals(commandLine, comparison));
    }

    /// <summary>
    /// A human-readable description of the runtime environment for the agent's system prompt: the OS,
    /// the shell commands run through, and the only command lines allowed.
    /// </summary>
    public string EnvironmentInfo
    {
        get
        {
            var os = _isWindows ? "Windows" : OperatingSystem.IsMacOS() ? "macOS" : "Linux/Unix";
            return
                $"You are running on {os} ({RuntimeInformation.OSDescription.Trim()}). " +
                "Terminal commands run with the workspace folder as the working directory. " +
                $"Only these exact command lines are allowed, with no other arguments: {AllowedList}. " +
                "Use them only to identify the current user or read the current date and time.";
        }
    }

    private string AllowedList => string.Join(", ", AllowedCommands.Select(c => $"'{c}'"));

    /// <summary>Runs an allowlisted command line in the workspace and returns its output.</summary>
    /// <param name="command">The command to run, e.g. <c>whoami</c> or <c>date</c>.</param>
    /// <param name="arguments">The arguments, e.g. <c>/t</c>. Together with the command they must exactly match an allowed line.</param>
    /// <param name="cancellationToken">A token to cancel the command.</param>
    /// <returns>The combined stdout/stderr and exit code, or a message when the command is not allowed.</returns>
    [Description("Run one of a few allowed read-only commands in the workspace and return its output: 'whoami' to get the current user, and 'date /t' / 'time /t' on Windows or 'date' on macOS/Linux to get the current date and time. Any other command or argument is rejected.")]
    public async Task<string> RunCommand(
        [Description("The command to run: 'whoami', 'date' or (Windows only) 'time'.")] string command,
        [Description("The arguments: '/t' for 'date' or 'time' on Windows; otherwise leave empty.")] string arguments = "",
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return "No command was provided.";
        }

        var commandLine = Match(command, arguments, _isWindows);
        if (commandLine is null)
        {
            return $"Command '{$"{command} {arguments}".Trim()}' is not allowed. Only these exact commands are permitted: {AllowedList}.";
        }

        var workspace = WorkspaceScope.Require().Root;

        var startInfo = new ProcessStartInfo
        {
            WorkingDirectory = workspace,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (!TryConfigureExecutable(startInfo, commandLine))
        {
            return $"Command '{commandLine}' is not available on this system.";
        }

        using var process = new Process { StartInfo = startInfo };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        try
        {
            process.Start();
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            return $"Failed to start '{commandLine}': {ex.Message}";
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(Timeout);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            return $"Command '{commandLine}' timed out after {Timeout.TotalSeconds:N0}s and was terminated.";
        }

        var sb = new StringBuilder();
        sb.AppendLine($"$ {commandLine}");
        sb.AppendLine($"Exit code: {process.ExitCode}");
        if (stdout.Length > 0)
        {
            sb.AppendLine("stdout:").Append(stdout);
        }

        if (stderr.Length > 0)
        {
            sb.AppendLine("stderr:").Append(stderr);
        }

        return sb.ToString();
    }

    /// <summary>Exposes this tool's methods as AI tools for an agent.</summary>
    public IList<AITool> AsTools() =>
    [
        AIFunctionFactory.Create(RunCommand),
    ];

    // Resolves the allowed command line to an absolute executable so neither the workspace (the working
    // directory) nor the inherited PATH can supply a look-alike. Returns false when the binary is missing.
    private bool TryConfigureExecutable(ProcessStartInfo startInfo, string commandLine)
    {
        var parts = commandLine.Split(' ');
        if (_isWindows)
        {
            if (parts.Length == 1)
            {
                startInfo.FileName = Path.Combine(Environment.SystemDirectory, $"{parts[0]}.exe");
                return File.Exists(startInfo.FileName);
            }

            // 'date /t' and 'time /t' exist only as cmd.exe built-ins, which win over any file of the same name.
            startInfo.FileName = Path.Combine(Environment.SystemDirectory, "cmd.exe");
            startInfo.ArgumentList.Add("/d");
            startInfo.ArgumentList.Add("/c");
            foreach (var part in parts)
            {
                startInfo.ArgumentList.Add(part);
            }

            startInfo.Environment["NoDefaultCurrentDirectoryInExePath"] = "1";
            return File.Exists(startInfo.FileName);
        }

        var executable = UnixSearchDirectories.Select(dir => Path.Combine(dir, parts[0])).FirstOrDefault(File.Exists);
        if (executable is null)
        {
            return false;
        }

        startInfo.FileName = executable;
        foreach (var part in parts.Skip(1))
        {
            startInfo.ArgumentList.Add(part);
        }

        return true;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort; the process may already be gone.
        }
    }
}
