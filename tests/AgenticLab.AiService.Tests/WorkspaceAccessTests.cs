using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Application.Skills;
using AgenticLab.AiService.Application.Tools;
using AgenticLab.AiService.Application.Workspace;
using AgenticLab.AiService.Demo.Agents;
using AgenticLab.AiService.Startup;
using AgenticLab.Extensibility.Agents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class WorkspaceAccessTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("agentic-lab-access-").FullName;
    private readonly string _sample = Directory.CreateTempSubdirectory("agentic-lab-sample-").FullName;

    public void Dispose()
    {
        Directory.Delete(_root, recursive: true);
        Directory.Delete(_sample, recursive: true);
    }

    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    private static void WriteSkill(string root, string name, string? allowedTools)
    {
        var folder = Directory.CreateDirectory(Path.Combine(root, "skills", name)).FullName;
        var tools = allowedTools is null ? string.Empty : $"allowed-tools: {allowedTools}\n";
        File.WriteAllText(Path.Combine(folder, "SKILL.md"), $"---\nname: {name}\ndescription: {name} skill\n{tools}---\nBody");
    }

    [Fact]
    public void FromConfiguration_DefaultsToLocal()
    {
        var access = WorkspaceAccess.FromConfiguration(Configuration());

        Assert.Equal(WorkspaceMode.Local, access.Mode);
        Assert.True(access.RequiresClientPath);
        Assert.False(access.ReadOnly);
    }

    [Fact]
    public void FromConfiguration_KeepsTheOlderEnabledSwitch() =>
        Assert.Equal(WorkspaceMode.Disabled,
            WorkspaceAccess.FromConfiguration(Configuration((WorkspaceAccess.EnabledKey, "false"))).Mode);

    [Fact]
    public void FromConfiguration_ModeWinsOverEnabledAndIgnoresCase()
    {
        var access = WorkspaceAccess.FromConfiguration(Configuration(
            (WorkspaceAccess.ModeKey, "readonlysample"), (WorkspaceAccess.EnabledKey, "false")));

        Assert.Equal(WorkspaceMode.ReadOnlySample, access.Mode);
        Assert.True(access.Enabled);
        Assert.False(access.RequiresClientPath);
        Assert.True(access.ReadOnly);
    }

    [Theory]
    [InlineData("Everything")]
    [InlineData("42")]
    public void FromConfiguration_RejectsAnUnknownMode(string mode) =>
        Assert.Throws<InvalidOperationException>(() =>
            WorkspaceAccess.FromConfiguration(Configuration((WorkspaceAccess.ModeKey, mode))));

    [Fact]
    public void TryBegin_FollowsTheMode()
    {
        using (var local = new WorkspaceAccess(WorkspaceMode.Local).TryBegin(_root))
        {
            Assert.Equal(Path.GetFullPath(_root), local?.Root);
        }

        Assert.Null(new WorkspaceAccess(WorkspaceMode.Disabled).TryBegin(_root));
        Assert.Null(WorkspaceScope.Current);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("/")]
    public void TryBegin_ReadOnlySampleIgnoresTheCallersPath(string? requested)
    {
        var access = new WorkspaceAccess(WorkspaceMode.ReadOnlySample, _sample);

        using var scope = access.TryBegin(requested ?? _root);

        Assert.Equal(Path.GetFullPath(_sample), scope?.Root);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SampleRoot_BlankMeansTheBundledDefault(string? configured) =>
        Assert.Equal(Path.Combine(AppContext.BaseDirectory, "sample-workspace"),
            new WorkspaceAccess(WorkspaceMode.ReadOnlySample, configured).SampleRoot);

    [Fact]
    public void WorkspaceAgents_SampleModeOffersOnlyAgentsWithKnownReadOnlyTools()
    {
        var agents = Directory.CreateDirectory(Path.Combine(_sample, "agents")).FullName;
        foreach (var (name, tools) in new[]
        {
            ("Reader", "[ReadFile, ListFiles]"),
            ("Writer", "[ReadFile, WriteFile]"),
            ("Unknown", "[ReadFile, vscode/openSimpleBrowser]"),
        })
        {
            File.WriteAllText(Path.Combine(agents, $"{name.ToLowerInvariant()}.agent.yaml"),
                $"name: {name}\ndescription: {name} agent\ntools: {tools}\npersona: You are {name}.\n");
        }

        IReadOnlyList<string> Offered(WorkspaceMode mode)
        {
            var access = new WorkspaceAccess(mode, _sample);
            var configuration = Configuration(
                ("AzureOpenAI:Endpoint", "https://unused.invalid"), ("AzureOpenAI:Deployment", "test"),
                ("AzureOpenAI:ApiKey", "not-used-by-this-test"));
            using var http = new HttpClient();
            var resolver = new WorkspaceAgentResolver(new ChatClientProvider(configuration), new WorkspaceAgentLoader(),
                new FileSystemTool(), new TerminalTool(configuration),
                new SkillsTool(new SkillLoader(access), new SkillMatcher()), new AskQuestionTool(), new WebFetchTool(http), access);
            using var scope = access.TryBegin(_sample);
            return resolver.ListAgents().Select(agent => agent.Name).Order().ToList();
        }

        Assert.Equal(["Reader"], Offered(WorkspaceMode.ReadOnlySample));
        Assert.Equal(["Reader", "Unknown", "Writer"], Offered(WorkspaceMode.Local));
    }

    [Fact]
    public void TryBegin_ReadOnlySampleFailsClosedWhenTheSampleIsMissing() =>
        Assert.Null(new WorkspaceAccess(WorkspaceMode.ReadOnlySample, Path.Combine(_sample, "missing")).TryBegin(_root));

    [Theory]
    [InlineData("ReadFile", true)]
    [InlineData("ListFiles", true)]
    [InlineData("readskill", true)]
    [InlineData("AskQuestion", true)]
    [InlineData("WriteFile", false)]
    [InlineData("DeleteFile", false)]
    [InlineData("RunCommand", false)]
    [InlineData("WebFetch", false)]
    public void AllowsTool_OnlyReadOnlyToolsInTheSampleMode(string tool, bool allowedInSample)
    {
        Assert.Equal(allowedInSample, new WorkspaceAccess(WorkspaceMode.ReadOnlySample).AllowsTool(tool));
        Assert.True(new WorkspaceAccess(WorkspaceMode.Local).AllowsTool(tool));
    }

    [Theory]
    [InlineData(null, true, true)]
    [InlineData("ReadOnlySample", true, false)]
    [InlineData("Disabled", false, false)]
    public void AddDemoAgents_RegistersWorkspaceAgentsByMode(string? mode, bool readOnlyAgents, bool coder)
    {
        var configuration = mode is null ? Configuration() : Configuration((WorkspaceAccess.ModeKey, mode));
        var registered = new ServiceCollection()
            .AddDemoAgents(configuration)
            .Where(descriptor => descriptor.ServiceType == typeof(IAgentDefinition))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        Assert.Equal(readOnlyAgents, registered.Contains(typeof(AskAgent)));
        Assert.Equal(readOnlyAgents, registered.Contains(typeof(PlanAgent)));
        Assert.Equal(coder, registered.Contains(typeof(CoderAgent)));
        Assert.Equal(typeof(ChatAgent), registered[0]);
        Assert.Contains(typeof(WikiAssistantAgent), registered);
        Assert.Contains(typeof(OrchestratorAgent), registered);
    }

    [Fact]
    public void SkillLoader_HidesSkillsNeedingWriteOrTerminalToolsInTheSampleMode()
    {
        WriteSkill(_sample, "review", "ReadFile, ListFiles");
        WriteSkill(_sample, "plain", allowedTools: null);
        WriteSkill(_sample, "terminal", "RunCommand");
        WriteSkill(_sample, "mixed", "[ReadFile, WriteFile]");
        var sample = new WorkspaceAccess(WorkspaceMode.ReadOnlySample, _sample);

        using (sample.TryBegin(null))
        {
            Assert.Equal(["plain", "review"], new SkillLoader(sample).Load().Select(s => s.Name));
            Assert.Equal(4, new SkillLoader().Load().Count);
            Assert.Equal(["ReadFile", "WriteFile"],
                new SkillLoader().Load().Single(s => s.Name == "mixed").AllowedTools);
        }
    }

    [Fact]
    public void BundledSampleWorkspace_OffersOnlyReadOnlySkillsInTheSampleMode()
    {
        var repo = new DirectoryInfo(AppContext.BaseDirectory);
        while (repo is not null && !File.Exists(Path.Combine(repo.FullName, "AgenticLab.slnx")))
        {
            repo = repo.Parent;
        }

        Assert.NotNull(repo);
        var sample = new WorkspaceAccess(WorkspaceMode.ReadOnlySample, Path.Combine(repo.FullName, "sample-workspace"));
        using (sample.TryBegin(null))
        {
            var names = new SkillLoader(sample).Load().Select(s => s.Name).ToList();
            Assert.Contains("review-code", names);
            Assert.DoesNotContain("get-date", names);
            Assert.DoesNotContain("whoami", names);
        }
    }
}
