using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using AgenticLab.Web.Flow;
using AgenticLab.Web.Learning;
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

    /// <summary>A tour identifies a learning-path task; it is carried in the link but is not a setting to apply.</summary>
    [Fact]
    public void Tour_IsCarriedButNotApplied()
    {
        Assert.Equal("/flow?tour=beginner%3Atools-disabled&agent=WikiAssistant",
            FlowDeepLink.Href(new FlowLinkRequest(Agent: "WikiAssistant", Tour: "beginner:tools-disabled")));
        Assert.Equal("beginner:tools-disabled",
            FlowDeepLink.Parse(null, null, null, null, null, null, null, null, " beginner:tools-disabled ").Tour);
        Assert.Null(FlowDeepLink.Parse(null, null, null, null, null, null, null, null).Tour);
        Assert.True(new FlowLinkRequest(Tour: "beginner:tools-disabled").IsEmpty);

        var view = LoadedView();
        FlowDeepLink.Apply(new FlowLinkRequest(Tour: "beginner:tools-disabled"), view);
        Assert.False(view.Guide.Active);
        Assert.Equal(string.Empty, view.Message);
    }

    /// <summary>The two Beginner exercises switch the Wikipedia tools off, then on again, and only pre-fill the question.</summary>
    [Fact]
    public void PathExercises_PrepareToolsOffThenOnWithoutSending()
    {
        var view = LoadedView();
        view.SelectedAgent = "MathTutor";
        var beginner = LearningPaths.Find("beginner")!;
        var disabled = beginner.Find("tools-disabled")!.Exercise!;
        var enabled = beginner.Find("tools-enabled")!.Exercise!;

        Assert.Null(GuidedTask.Check(disabled, view.Roster));
        FlowDeepLink.Apply(disabled.Setup, view);
        Assert.Equal("WikiAssistant", view.SelectedAgent);
        Assert.False(view.Options.IsToolEnabled("SearchWiki") || view.Options.IsToolEnabled("GetWikiPage"));
        Assert.Equal(2, view.Options.DisabledToolsOrNull!.Count);
        Assert.True(view.Diagram.ShowTools && view.Diagram.ShowModel);
        Assert.Equal("Who is the king of Norway", view.Message);

        Assert.Null(GuidedTask.Check(enabled, view.Roster));
        FlowDeepLink.Apply(enabled.Setup, view);
        Assert.Null(view.Options.DisabledToolsOrNull);
        Assert.Equal("Who is the king of Norway", view.Message);
    }

    /// <summary>An exercise is reported as unavailable when its host lacks the agent or the agent lacks a tool.</summary>
    [Fact]
    public void GuidedTask_ReportsAMissingAgentOrTool()
    {
        var exercise = LearningPaths.Find("beginner")!.Find("tools-disabled")!.Exercise!;
        var view = LoadedView();

        view.HostKey = "chatgpt";
        Assert.Equal("The WikiAssistant agent is not available here.", GuidedTask.Check(exercise, view.Roster));
        Assert.Null(GuidedTask.Check(exercise with { Setup = exercise.Setup with { Host = "default" } }, view.Roster));
        Assert.NotNull(GuidedTask.Check(exercise with { Setup = exercise.Setup with { Host = "unknown" } }, view.Roster));

        view.HostKey = "default";
        view.Roster.SetAgents([new("WikiAssistant", "d", ["SearchWiki"])]);
        Assert.Equal("The WikiAssistant agent does not have GetWikiPage here.", GuidedTask.Check(exercise, view.Roster));

        view.Roster.SetAgents([]);
        Assert.Equal("The WikiAssistant agent is not available here.", GuidedTask.Check(exercise, view.Roster));
    }

    /// <summary>The task keeps its place while a concept is read or the concept index is browsed.</summary>
    [Fact]
    public void GuidedTask_KeepsTheTaskWhileConceptsAreBrowsed()
    {
        var view = LoadedView();
        var path = LearningPaths.Find("beginner")!;
        var step = path.Find("tools-enabled")!;
        Assert.False(view.Guide.Active);
        Assert.False(view.Guide.ShowingTask);

        var version = view.ContentVersion;
        view.Guide.Start(path, step, unavailable: null);
        Assert.True(view.Guide.Active && view.Guide.ShowingTask);
        Assert.True(view.ContentVersion > version);
        Assert.Equal(string.Empty, view.Message);
        Assert.Null(view.Options.DisabledToolsOrNull);

        view.Concepts.OpenConcept("tools");
        Assert.NotNull(view.Concepts.ActiveConcept);
        Assert.False(view.Guide.ShowingTask);
        view.Concepts.CloseConcept();
        Assert.True(view.Guide.ShowingTask);

        view.Guide.BrowseConcepts();
        Assert.False(view.Guide.ShowingTask);
        view.Guide.ShowTask();
        Assert.True(view.Guide.ShowingTask);
        Assert.Same(step, view.Guide.Step);
        Assert.Same(path, view.Guide.Path);

        view.Guide.Start(path, step, "The agent list could not be loaded.");
        Assert.Equal("The agent list could not be loaded.", view.Guide.Unavailable);
    }

    /// <summary>New Wikipedia exercises select their host even when a different host was previously selected.</summary>
    [Theory]
    [InlineData("intermediate", "tool-selection", FlowMode.Auto, false, false)]
    [InlineData("intermediate", "pause-and-step", FlowMode.Manual, true, false)]
    [InlineData("intermediate", "replay", FlowMode.Auto, false, false)]
    [InlineData("advanced", "captured-context", FlowMode.Auto, false, true)]
    internal void NewExercises_PrepareTheirControls(string pathId, string stepId, FlowMode mode, bool pause, bool expand)
    {
        var view = LoadedView();
        view.HostKey = "chatgpt";
        var exercise = LearningPaths.Find(pathId)!.Find(stepId)!.Exercise!;
        Assert.Null(GuidedTask.Check(exercise, view.Roster));
        Assert.Equal(new FlowLinkOutcome(true, true), FlowDeepLink.Apply(exercise.Setup, view));
        Assert.Equal("default", view.HostKey);
        Assert.Equal("WikiAssistant", view.SelectedAgent);
        Assert.Equal(mode, view.Options.Mode);
        Assert.Equal(pause, view.Options.IsBreakpointEnabled("before-tool"));
        Assert.Equal(expand, view.Diagram.ExpandHarness);
        Assert.True(view.Options.IsToolEnabled("SearchWiki"));
        Assert.Equal(stepId != "tool-selection", view.Options.IsToolEnabled("GetWikiPage"));
        Assert.Equal(exercise.Setup.Prompt, view.Message);
        Assert.False(view.Guide.Active);
    }

    /// <summary>Security exercises require the optional host and match its real scripted agents and tool lists.</summary>
    [Theory]
    [InlineData("security-scenario", "LeakyAssistant")]
    [InlineData("defended-boundary", "DefendedAssistant")]
    public void SecurityExercises_RequireTheirHostAndPrepareBeforeToolPause(string stepId, string agentName)
    {
        var view = LoadedView();
        var exercise = LearningPaths.Find("advanced")!.Find(stepId)!.Exercise!;
        Assert.NotNull(GuidedTask.Check(exercise, view.Roster));
        AgenticLab.Examples.Security.Agents.ScenarioAgentBase agent = agentName == "LeakyAssistant"
            ? new AgenticLab.Examples.Security.Agents.LeakyAssistantAgent()
            : new AgenticLab.Examples.Security.Agents.DefendedAssistantAgent();
        view.Roster.SetExamples([new AgenticLab.Examples.Security.SecurityExample().Manifest]);
        view.Roster.SetVendors([new("security", "Security Lab", "", [new(agent.Name, agent.Name)], "security")]);
        view.Roster.SetAgents([new(agent.Name, agent.Description, agent.Tools.Select(tool => tool.Name).ToArray())]);
        Assert.Null(GuidedTask.Check(exercise, view.Roster));
        Assert.Equal(new FlowLinkOutcome(true, true), FlowDeepLink.Apply(exercise.Setup, view));
        Assert.Equal(agentName, view.SelectedAgent);
        Assert.Equal("security", view.HostKey);
        Assert.Equal(["before-tool"], view.Options.Breakpoints);
        Assert.Equal(FlowMode.Auto, view.Options.Mode);
        Assert.Null(view.Options.DisabledToolsOrNull);
        Assert.Equal(exercise.Setup.Prompt, view.Message);
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
