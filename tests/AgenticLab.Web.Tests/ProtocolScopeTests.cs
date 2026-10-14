using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using AgenticLab.Web.Flow;
using Xunit;

namespace AgenticLab.Web.Tests;

/// <summary>
/// Protects the per-agent protocol boundaries the harness boxes and Discovery claim: the A2A and MCP
/// catalogues show only what the service bounded the selected agent to, and Discovery groups the rest
/// by owner instead of hiding it.
/// </summary>
public sealed class ProtocolScopeTests
{
    [Fact]
    public async Task Catalogues_ShowOnlyTheAgentsBoundedRemoteAgentsAndTools()
    {
        var view = new FlowViewState(new ConceptCatalog(new TestEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.Roster.SetAgents(
        [
            new AgentInfo("Orchestrator", "d", ["Calculate", "DelegateToAgent"], SupportsA2A: true, RemoteAgentNames: ["research", "poet"]),
            new AgentInfo("TimeKeeper", "d", ["GetCurrentTime"], SupportsMcp: true),
            new AgentInfo("Chat", "d", []),
        ]);
        using var handler = new CatalogueHandler();
        using var http = new HttpClient(handler) { BaseAddress = new Uri("http://test") };
        var catalogs = new WorkspaceCatalogs(new AiServiceClient(http), view, () => Task.CompletedTask);

        view.SelectedAgent = "Orchestrator";
        await catalogs.RefreshKnownA2AAsync();
        await catalogs.RefreshKnownMcpAsync();
        Assert.Equal(new[] { "research", "poet" }, catalogs.KnownA2A.Select(agent => agent.Name));
        Assert.Empty(catalogs.KnownMcp);

        view.SelectedAgent = "TimeKeeper";
        await catalogs.RefreshKnownA2AAsync();
        await catalogs.RefreshKnownMcpAsync();
        Assert.Empty(catalogs.KnownA2A);
        Assert.Equal("GetCurrentTime", Assert.Single(catalogs.KnownMcp).Name);

        view.SelectedAgent = "Chat";
        await catalogs.RefreshKnownA2AAsync();
        await catalogs.RefreshKnownMcpAsync();
        Assert.Empty(catalogs.KnownA2A);
        Assert.Empty(catalogs.KnownMcp);
    }

    [Fact]
    public void DiscoveryGrouping_ScopesToTheAgentAndGroupsTheRestByOwner()
    {
        DiscoveryItemDto[] items =
        [
            new("research", "r"), new("poet", "p"),
            new("windfarm-risk", "w", "windfarm"), new("other-tool", "o", "unknown"),
        ];
        string? Title(string id) => id == "windfarm" ? "Windfarm Operations" : null;
        var a2a = DiscoveryGrouping.ComparerFor("a2a");

        var scoped = DiscoveryGrouping.Group(items, ["poet", "Research"], Title, a2a);
        Assert.True(scoped.Scoped);
        Assert.Equal(new[] { "research", "poet" }, scoped.Available.Select(item => item.Name));
        Assert.Equal(new[] { "unknown", "Windfarm Operations" }, scoped.Groups.Select(group => group.Title));
        Assert.Equal("windfarm-risk", Assert.Single(scoped.Groups[1].Items).Name);

        var unscoped = DiscoveryGrouping.Group(items, null, Title, a2a);
        Assert.False(unscoped.Scoped);
        Assert.Empty(unscoped.Available);
        Assert.Equal(new[] { DiscoveryGrouping.SharedTitle, "unknown", "Windfarm Operations" }, unscoped.Groups.Select(group => group.Title));
        Assert.Equal(new[] { "research", "poet" }, unscoped.Groups[0].Items.Select(item => item.Name));

        var nothingRelevant = DiscoveryGrouping.Group(items, [], Title, a2a);
        Assert.True(nothingRelevant.Scoped);
        Assert.Empty(nothingRelevant.Available);
        Assert.Equal(items.Length, nothingRelevant.Groups.Sum(group => group.Items.Count));
    }

    [Fact]
    public void DiscoveryScope_KeepsProtocolsApartWhenNamesOverlap()
    {
        // TimeKeeper holds the MCP time tool; an A2A specialist happens to share the name.
        var timeKeeper = new DiscoveryScope(["GetCurrentTime"], []);
        DiscoveryItemDto[] mcpItems = [new("GetCurrentTime", "t")];
        DiscoveryItemDto[] a2aItems = [new("GetCurrentTime", "impostor"), new("research", "r")];
        Assert.True(timeKeeper.IsScoped);
        Assert.Single(DiscoveryGrouping.Group(mcpItems, timeKeeper.For("mcp"), _ => null, DiscoveryGrouping.ComparerFor("mcp")).Available);
        var a2aView = DiscoveryGrouping.Group(a2aItems, timeKeeper.For("a2a"), _ => null, DiscoveryGrouping.ComparerFor("a2a"));
        Assert.True(a2aView.Scoped);
        Assert.Empty(a2aView.Available);

        // Orchestrator delegates to the look-alike agent but holds no MCP tools.
        var orchestrator = new DiscoveryScope([], ["GetCurrentTime"]);
        Assert.Empty(DiscoveryGrouping.Group(mcpItems, orchestrator.For("mcp"), _ => null, DiscoveryGrouping.ComparerFor("mcp")).Available);
        Assert.Equal("GetCurrentTime", Assert.Single(DiscoveryGrouping.Group(a2aItems, orchestrator.For("a2a"), _ => null, DiscoveryGrouping.ComparerFor("a2a")).Available).Name);

        var chat = new DiscoveryScope([], []);
        Assert.False(chat.IsScoped);
        Assert.Null(chat.For("mcp"));
        Assert.Null(chat.For("a2a"));
    }

    [Fact]
    public void DiscoveryGrouping_MatchesMcpToolsExactlyAndA2AAgentsCaseInsensitively()
    {
        DiscoveryItemDto[] tools = [new("GetCurrentTime", "t"), new("getcurrenttime", "lower")];
        var mcp = DiscoveryGrouping.Group(tools, ["GetCurrentTime"], _ => null, DiscoveryGrouping.ComparerFor("mcp"));
        Assert.Equal("GetCurrentTime", Assert.Single(mcp.Available).Name);
        Assert.Equal("getcurrenttime", Assert.Single(Assert.Single(mcp.Groups).Items).Name);

        DiscoveryItemDto[] agents = [new("Research", "r")];
        var a2a = DiscoveryGrouping.Group(agents, ["research"], _ => null, DiscoveryGrouping.ComparerFor("a2a"));
        Assert.Equal("Research", Assert.Single(a2a.Available).Name);
    }

    private sealed class TestEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AgenticLab.Web.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class CatalogueHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/a2a" => """{"agents":[{"name":"research","description":"r"},{"name":"poet","description":"p"},{"name":"windfarm-risk","description":"w"}]}""",
                "/mcp" => """{"servers":[{"name":"mcpserver","tools":[{"name":"GetCurrentTime","description":"t"},{"name":"WindfarmAlarms","description":"a"}]}]}""",
                _ => throw new InvalidOperationException($"Unexpected request: {request.RequestUri}"),
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") });
        }
    }
}
