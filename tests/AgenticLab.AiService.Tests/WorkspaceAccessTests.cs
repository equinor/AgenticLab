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

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static IConfiguration Configuration(string? enabled) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(enabled is null ? [] : [new(WorkspaceAccess.EnabledKey, enabled)])
            .Build();

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void FromConfiguration_DefaultsToEnabled(string? configured, bool expected) =>
        Assert.Equal(expected, WorkspaceAccess.FromConfiguration(Configuration(configured)).Enabled);

    [Fact]
    public void TryBegin_OpensAnExistingDirectoryOnlyWhenEnabled()
    {
        using (var scope = new WorkspaceAccess(enabled: true).TryBegin(_root))
        {
            Assert.NotNull(scope);
            Assert.Equal(Path.GetFullPath(_root), scope.Root);
        }

        Assert.Null(new WorkspaceAccess(enabled: false).TryBegin(_root));
        Assert.Null(WorkspaceScope.Current);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("false", false)]
    public void AddDemoAgents_RegistersWorkspaceAgentsOnlyWhenEnabled(string? configured, bool expected)
    {
        var registered = new ServiceCollection()
            .AddDemoAgents(Configuration(configured))
            .Where(descriptor => descriptor.ServiceType == typeof(IAgentDefinition))
            .Select(descriptor => descriptor.ImplementationType)
            .ToList();

        Assert.Equal(expected, registered.Contains(typeof(AskAgent)));
        Assert.Equal(expected, registered.Contains(typeof(PlanAgent)));
        Assert.Equal(expected, registered.Contains(typeof(CoderAgent)));
        Assert.Equal(typeof(ChatAgent), registered[0]);
        Assert.Contains(typeof(WikiAssistantAgent), registered);
        Assert.Contains(typeof(OrchestratorAgent), registered);
    }
}
