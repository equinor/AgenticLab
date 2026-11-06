using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using AgenticLab.Web.Flow;
using Xunit;

namespace AgenticLab.Web.Tests;

/// <summary>
/// Protects the /flow deep-link contract: links pre-select a host, agent, view and run options and pre-fill
/// the composer, while unknown values are ignored and nothing is ever sent.
/// </summary>
public sealed class FlowDeepLinkTests
{
    private static FlowViewState LoadedView()
    {
        var view = new FlowViewState(new ConceptCatalog(new LinkEnvironment(), NullLogger<ConceptCatalog>.Instance));
        view.Roster.SetExamples([new AgenticLab.Examples.ChatGpt.ChatGptExample().Manifest]);
        view.Roster.SetVendors([
            new("default", "Default", "", [new("WikiAssistant", "Wikipedia"), new("MathTutor", "Math")]),
            new("chatgpt", "ChatGPT", "demo", [new("ChatGpt", "Chat")], "chatgpt"),
        ]);
        view.Roster.SetAgents([
            new("WikiAssistant", "d", ["SearchWiki", "GetWikiPage"], SuggestedPrompts: ["Who was Ada Lovelace?"]),
            new("MathTutor", "d", ["Calculate"]),
            new("ChatGpt", "d", ["SearchWiki", "GetWikiPage", "Calculate"]),
        ]);
        view.InitSelectedAgent("WikiAssistant");
        return view;
    }

    [Fact]
    public void Href_RoundTripsThroughParse()
    {
        var request = new FlowLinkRequest("chatgpt", "ChatGpt", "What is 2 + 2? & more", DiagramPreset.Technical, true,
            FlowMode.Manual, ["before-tool", "after-tool"], ["-SearchWiki"]);

        var href = FlowDeepLink.Href(request);
        Assert.StartsWith("/flow?", href);
        Assert.Contains("prompt=What%20is%202%20%2B%202%3F%20%26%20more", href);

        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(href[href.IndexOf('?')..]);
        string? Value(string name) => query.TryGetValue(name, out var values) ? values.ToString() : null;
        var parsed = FlowDeepLink.Parse(Value("host"), Value("agent"), Value("prompt"), Value("view"), Value("harness"),
            Value("mode"), Value("breakpoints"), Value("tools"));
        Assert.Equal(request with { Breakpoints = null, Tools = null }, parsed with { Breakpoints = null, Tools = null });
        Assert.Equal(request.Breakpoints, parsed.Breakpoints);
        Assert.Equal(request.Tools, parsed.Tools);
        Assert.Equal("/flow", FlowDeepLink.Href(new FlowLinkRequest()));
        Assert.True(FlowDeepLink.Parse(null, " ", "", null, null, null, null, null).IsEmpty);
    }

    [Fact]
    public void Apply_SelectsHostAgentViewOptionsAndPrefillsWithoutSending()
    {
        var view = LoadedView();
        var request = FlowDeepLink.Parse("ChatGPT", "chatgpt", "Look up Norway.", "technical", "1", "manual",
            "before-tool,bogus", "-SearchWiki,Calculate,-Missing");

        var outcome = FlowDeepLink.Apply(request, view);

        Assert.Equal(new FlowLinkOutcome(true, true), outcome);
        Assert.Equal("chatgpt", view.HostKey);
        Assert.Equal("ChatGpt", view.SelectedAgent);
        // The technical preset's values apply; expanding the harness makes the combination custom.
        Assert.True(view.Diagram.ShowModel && view.Diagram.ShowLoop && view.Diagram.ShowTechnicalLabels && view.Diagram.ShowTools);
        Assert.True(view.Diagram.ExpandHarness);
        Assert.Null(view.Diagram.Preset);
        Assert.Equal(FlowMode.Manual, view.Options.Mode);
        Assert.Equal(["before-tool"], view.Options.Breakpoints);
        Assert.False(view.Options.IsToolEnabled("SearchWiki"));
        Assert.True(view.Options.IsToolEnabled("GetWikiPage"));
        Assert.Equal(["SearchWiki"], view.Options.DisabledToolsOrNull);
        Assert.Equal("Look up Norway.", view.Message);
    }

    [Fact]
    public void Apply_IgnoresUnknownHostAndAgentButKeepsTheRest()
    {
        var view = LoadedView();

        var outcome = FlowDeepLink.Apply(FlowDeepLink.Parse("nope", "Ghost", "Hello", "basic", null, "auto", null, "-Calculate"), view);

        Assert.Equal(new FlowLinkOutcome(false, false), outcome);
        Assert.Equal("default", view.HostKey);
        Assert.Equal("WikiAssistant", view.SelectedAgent);
        Assert.Equal(DiagramPreset.Basic, view.Diagram.Preset);
        Assert.Null(view.Options.DisabledToolsOrNull);
        Assert.Equal("Hello", view.Message);
    }

    [Fact]
    public void Apply_HostWithoutAgentPicksTheHostDefaultAndTruncatesToTheChatLimit()
    {
        var view = LoadedView();
        view.ChatLimits.Set(new ChatLimitsInfo(5));

        var outcome = FlowDeepLink.Apply(new FlowLinkRequest(Host: "chatgpt", Prompt: "0123456789"), view);

        Assert.Equal(new FlowLinkOutcome(true, false), outcome);
        Assert.Equal("ChatGpt", view.SelectedAgent);
        Assert.Equal("01234", view.Message);
    }

    [Fact]
    public void SelectedAgent_ExposesSuggestedPromptsOrNone()
    {
        var view = LoadedView();
        Assert.Equal(["Who was Ada Lovelace?"], view.Agent.SuggestedPrompts);
        view.SelectedAgent = "MathTutor";
        Assert.Empty(view.Agent.SuggestedPrompts);
    }

    private sealed class LinkEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AgenticLab.Web.Tests";
        public string EnvironmentName { get; set; } = "Testing";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}
