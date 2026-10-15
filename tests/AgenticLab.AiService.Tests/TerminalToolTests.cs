using AgenticLab.AiService.Application.Tools;
using AgenticLab.AiService.Application.Workspace;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class TerminalToolTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agentic-lab-terminal-").FullName;

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Theory]
    [InlineData(true, "whoami", "", "whoami")]
    [InlineData(true, "date", "/t", "date /t")]
    [InlineData(true, "TIME", " /T ", "time /t")]
    [InlineData(true, "date /t", "", "date /t")]
    [InlineData(false, "whoami", "", "whoami")]
    [InlineData(false, " date ", "  ", "date")]
    public void Match_AcceptsOnlyTheExactCommandLinesForTheOs(bool isWindows, string command, string arguments, string expected) =>
        Assert.Equal(expected, TerminalTool.Match(command, arguments, isWindows));

    [Theory]
    [InlineData(true, "date", "")]
    [InlineData(true, "time", "")]
    [InlineData(true, "whoami", "/all")]
    [InlineData(true, "powershell", "-NoProfile -Command Get-Date")]
    [InlineData(true, "whoami", "& dir")]
    [InlineData(true, "dir", "")]
    [InlineData(false, "date", "/t")]
    [InlineData(false, "DATE", "")]
    [InlineData(false, "date", "-s 2020-01-01")]
    [InlineData(false, "date", "+%F")]
    [InlineData(false, "whoami", "; ls")]
    [InlineData(false, "git", "status")]
    [InlineData(false, "dotnet", "--version")]
    public void Match_RejectsEverythingElse(bool isWindows, string command, string arguments) =>
        Assert.Null(TerminalTool.Match(command, arguments, isWindows));

    [Fact]
    public void AllowedCommandsFor_ListsOnlyUserAndDateTimeCommands()
    {
        Assert.Equal(["whoami", "date /t", "time /t"], TerminalTool.AllowedCommandsFor(isWindows: true));
        Assert.Equal(["whoami", "date"], TerminalTool.AllowedCommandsFor(isWindows: false));
    }

    [Fact]
    public void EnvironmentInfo_NamesOnlyTheAllowedCommands()
    {
        var info = new TerminalTool().EnvironmentInfo;

        foreach (var command in new TerminalTool().AllowedCommands)
        {
            Assert.Contains($"'{command}'", info);
        }

        Assert.DoesNotContain("dotnet", info);
        Assert.DoesNotContain("powershell", info);
    }

    [Fact]
    public async Task RunCommand_RunsTheAllowedCommandsOnThisOs()
    {
        var tool = new TerminalTool();
        using var scope = WorkspaceScope.Begin(_root);

        foreach (var commandLine in tool.AllowedCommands)
        {
            var parts = commandLine.Split(' ', 2);
            var output = await tool.RunCommand(parts[0], parts.Length > 1 ? parts[1] : "");

            Assert.Contains("Exit code: 0", output);
            Assert.Contains("stdout:", output);
        }
    }

    [Fact]
    public async Task RunCommand_IgnoresALookAlikeExecutableInTheWorkspace()
    {
        var fakeName = OperatingSystem.IsWindows() ? "whoami.cmd" : "whoami";
        var fake = Path.Combine(_root, fakeName);
        File.WriteAllText(fake, OperatingSystem.IsWindows() ? "@echo HIJACKED\r\n" : "#!/bin/sh\necho HIJACKED\n");
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(fake, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        using var scope = WorkspaceScope.Begin(_root);

        var output = await new TerminalTool().RunCommand("whoami");

        Assert.Contains("Exit code: 0", output);
        Assert.DoesNotContain("HIJACKED", output);
    }

    [Fact]
    public async Task RunCommand_RejectsADisallowedCommandWithoutRunningIt()
    {
        using var scope = WorkspaceScope.Begin(_root);

        var output = await new TerminalTool().RunCommand("git", "status");

        Assert.StartsWith("Command 'git status' is not allowed.", output);
        Assert.DoesNotContain("Exit code", output);
    }
}
