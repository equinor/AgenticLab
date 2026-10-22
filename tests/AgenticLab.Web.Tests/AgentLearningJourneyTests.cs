using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using AgenticLab.Web;
using AgenticLab.Web.Learning;
using Xunit;

namespace AgenticLab.Web.Tests;

/// <summary>Protects journey permalinks, navigation and references to the shipped learning content.</summary>
public sealed class AgentLearningJourneyTests
{
    /// <summary>Every new request returns to context management; execution checks tools before results or a stop.</summary>
    [Fact]
    public void TurnExample_MapsStepsOntoPersistentLoopNodes()
    {
        var story = new AgentTurnStory();
        Assert.Equal("context", story.Step.LoopNodeId);
        Assert.Equal(
            ["context", "context", "model", "execute", "execute", "context", "model", "answer"],
            story.Scenario.Steps.Select(step => step.LoopNodeId).ToArray());
        foreach (var scenario in AgentTurnStory.Scenarios)
        {
            Assert.All(scenario.Steps, step => Assert.Contains(step.LoopNodeId,
                new[] { "context", "model", "execute", "answer" }));
            Assert.Equal("context", Assert.Single(scenario.Steps, step => step.Id == "request-1").LoopNodeId);
            if (scenario.Id is "cancelled" or "limit")
                Assert.Equal("answer", scenario.Steps[^1].LoopNodeId);
        }
    }

    /// <summary>Only a successful read supplies file content on the next model request.</summary>
    [Fact]
    public void TurnExample_SeparatesToolRequestsExecutionAndContext()
    {
        Assert.DoesNotContain(AgentTurnStory.FirstRequest.Messages, message => message.Text.Contains(AgentTurnStory.FileContent));
        Assert.Equal("path", Assert.Single(AgentTurnStory.ReadTool.Arguments).Name);
        Assert.Equal("meeting-notes.txt", AgentTurnStory.ReadCall.Arguments["path"]);
        foreach (var scenario in AgentTurnStory.Scenarios.Where(item => item.Id is "read" or "denied" or "missing"))
        {
            var request = Assert.Single(scenario.Steps, step => step.Id == "request-2").Request!;
            var result = Assert.Single(request.Messages, message => message.Role == "tool");
            Assert.Equal(AgentTurnStory.ReadCall.Id, result.ToolCallId);
            Assert.Equal(AgentTurnStory.ReadCall, Assert.Single(request.Messages, message => message.ToolCall is not null).ToolCall);
            Assert.Equal(scenario.Id == "read", result.Text == AgentTurnStory.FileContent);
            Assert.Equal(scenario.Id != "denied", scenario.Steps.Any(step => step.ExecutesTool));
            Assert.Equal(scenario.Id == "read" ? "answered" : "incomplete", scenario.Steps[^1].Outcome);
        }
        Assert.Contains("Owner: not stated", AgentTurnStory.Answer);
        Assert.Contains("Deadline: not stated", AgentTurnStory.Answer);
    }

    /// <summary>Cancellation, limits and already-supplied notes require no executed tool.</summary>
    [Theory]
    [InlineData("cancelled", "cancelled")]
    [InlineData("limit", "limit")]
    [InlineData("provided", "answered")]
    public void TurnExample_NoToolBranchesHaveExplicitOutcomes(string id, string outcome)
    {
        var scenario = AgentTurnStory.Scenarios.Single(item => item.Id == id);
        Assert.DoesNotContain(scenario.Steps, step => step.ExecutesTool);
        Assert.Single(scenario.Steps, step => step.Request is not null);
        Assert.Equal(outcome, scenario.Steps[^1].Outcome);
        if (id == "provided")
        {
            Assert.Contains(scenario.Steps[1].Request!.Messages, message => message.Text.Contains(AgentTurnStory.FileContent));
            Assert.Empty(scenario.Steps[1].Request!.Tools);
        }
    }

    /// <summary>Scenario changes reset the sequence; restart preserves the selected case.</summary>
    [Fact]
    public void TurnExample_NavigationIsBoundedAndScenarioLocal()
    {
        var story = new AgentTurnStory();
        story.Move(int.MinValue);
        Assert.Equal(0, story.Beat);
        Assert.False(story.CanPrevious);
        story.Complete();
        Assert.True(story.ShowAll);
        Assert.False(story.CanNext);
        story.Select("denied");
        Assert.Equal(0, story.Beat);
        Assert.False(story.ShowAll);
        story.Move(int.MaxValue);
        Assert.Equal(story.Scenario.Steps.Count - 1, story.Beat);
        Assert.False(story.ShowAll);
        story.Complete();
        story.Move(int.MaxValue);
        Assert.False(story.ShowAll);
        story.Restart();
        Assert.Equal("denied", story.Scenario.Id);
        Assert.Equal(0, story.Beat);
        story.Select("unknown");
        Assert.Equal("denied", story.Scenario.Id);
        Assert.All(AgentTurnStory.Scenarios, scenario =>
        {
            Assert.Equal(scenario.Steps.Count, scenario.Steps.Select(step => step.Id).Distinct().Count());
            Assert.All(scenario.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step.Explanation)));
        });
    }

    /// <summary>The teaching vocabulary separates model decisions from host-managed execution.</summary>
    [Fact]
    public void AgentDefinition_DistinguishesHostAndModel()
    {
        var stage = AgentLearningJourney.Resolve("model-to-agent");
        Assert.Contains("Agent = Harness + Model", stage.Takeaway);
        Assert.Equal("Harness", AgentLearningJourney.Node("harness").Title);
        Assert.Equal("Context management", AgentLearningJourney.Resolve("inside-the-harness").Title);
        Assert.Equal("Execution", AgentLearningJourney.Node("loop-execute").Title);
        Assert.Contains("Generates responses", AgentLearningJourney.Node("model").Detail);
    }

    /// <summary>Visible stages retain their identifiers and exclude the product-specific lesson.</summary>
    [Fact]
    public void Stages_PreserveOrderedPermalinks()
    {
        Assert.Equal(
                ["why-agents", "model-to-agent", "inside-the-harness", "tool-calling", "agent-loop", "anatomy-of-agent", "agents-everywhere", "agent-landscape", "wider-ecosystem", "where-to-run", "what-can-go-wrong", "run-and-improve"],
            AgentLearningJourney.Stages.Select(stage => stage.Id).ToArray());
    }

    /// <summary>Agent, shared foundations and hosting group their subtopics without changing lesson order.</summary>
    [Fact]
    public void Hierarchy_GroupsSubtopicsWithoutChangingReadingOrder()
    {
        Assert.Equal(
            ["why-agents", "model-to-agent", "agents-everywhere", "where-to-run"],
            AgentLearningJourney.RootStages.Select(stage => stage.Id).ToArray());
        Assert.Equal(
            ["inside-the-harness", "tool-calling", "agent-loop", "anatomy-of-agent"],
            AgentLearningJourney.Children("model-to-agent").Select(stage => stage.Id).ToArray());
        Assert.Equal(
            ["agent-landscape", "wider-ecosystem"],
            AgentLearningJourney.Children("agents-everywhere").Select(stage => stage.Id).ToArray());
        Assert.Equal(
            ["what-can-go-wrong", "run-and-improve"],
            AgentLearningJourney.Children("where-to-run").Select(stage => stage.Id).ToArray());
        Assert.Empty(AgentLearningJourney.Children("why-agents"));
        Assert.Equal(12, AgentLearningJourney.Stages.Count);
        Assert.Equal(AgentLearningJourney.Stages,
            AgentLearningJourney.RootStages.SelectMany(root => new[] { root }.Concat(AgentLearningJourney.Children(root.Id))).ToArray());
        Assert.Equal("inside-the-harness", AgentLearningJourney.Move("model-to-agent", 1).Id);
        Assert.Equal("agents-everywhere", AgentLearningJourney.Move("anatomy-of-agent", 1).Id);
        Assert.Equal("anatomy-of-agent", AgentLearningJourney.Move("agents-everywhere", -1).Id);
    }

    /// <summary>Root and child numbering shares the same hierarchy as the navigation rail.</summary>
    [Theory]
    [InlineData("why-agents", "1")]
    [InlineData("model-to-agent", "2")]
    [InlineData("inside-the-harness", "2.1")]
    [InlineData("tool-calling", "2.2")]
    [InlineData("agent-loop", "2.3")]
    [InlineData("anatomy-of-agent", "2.4")]
    [InlineData("agents-everywhere", "3")]
    [InlineData("agent-landscape", "3.1")]
    [InlineData("wider-ecosystem", "3.2")]
    [InlineData("where-to-run", "4")]
    [InlineData("what-can-go-wrong", "4.1")]
    [InlineData("run-and-improve", "4.2")]
    [InlineData("AGENT-LOOP", "2.3")]
    [InlineData("map-to-foundry", "1")]
    [InlineData(null, "1")]
    public void Hierarchy_NumbersRootsAndSubtopics(string? stageId, string expected)
    {
        Assert.Equal(expected, AgentLearningJourney.Number(stageId));
    }

    /// <summary>Intro slides match their diagrams, with valid connections and progressively added actors.</summary>
    [Fact]
    public void Introduction_HasAlignedSlidesAndValidDiagramExchanges()
    {
        var steps = AgentLearningJourney.IntroductionSteps;
        var diagrams = AgentLearningJourney.IntroductionDiagrams;
        Assert.Equal(["chatbot", "agent", "chat-interface", "chat-to-agent"], steps.Select(step => step.Id).ToArray());
        Assert.Equal(steps.Select(step => step.Id), diagrams.Select(diagram => diagram.Id));
        Assert.Equal([2, 3, 4, 4], diagrams.Select(diagram => diagram.Actors.Count).ToArray());

        foreach (var diagram in diagrams)
        {
            var actorIds = diagram.Actors.Select(actor => actor.Id).ToArray();
            Assert.Equal(actorIds.Length, actorIds.Distinct().Count());
            Assert.All(diagram.Exchanges, exchange =>
            {
                Assert.Contains(exchange.From, actorIds);
                Assert.Contains(exchange.To, actorIds);
                Assert.NotEqual(exchange.From, exchange.To);
            });
        }
    }

    /// <summary>Missing and retired links always resolve to a usable first stage.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("retired-stage")]
    [InlineData("map-to-foundry")]
    [InlineData("MAP-TO-FOUNDRY")]
    public void Resolve_FallsBackToFirstStage(string? id)
    {
        Assert.Same(AgentLearningJourney.Stages[0], AgentLearningJourney.Resolve(id));
    }

    /// <summary>Query-string identifiers accept mixed casing.</summary>
    [Fact]
    public void Resolve_IsCaseInsensitive()
    {
        Assert.Equal("agent-loop", AgentLearningJourney.Resolve("AGENT-LOOP").Id);
    }

    /// <summary>Adjacent and extreme moves stay within the ordered journey.</summary>
    [Fact]
    public void Move_NavigatesEveryStageAndClampsBoundaries()
    {
        var stages = AgentLearningJourney.Stages;
        for (var index = 0; index < stages.Count; index++)
        {
            Assert.Same(stages[Math.Max(0, index - 1)], AgentLearningJourney.Move(stages[index].Id, -1));
            Assert.Same(stages[Math.Min(stages.Count - 1, index + 1)], AgentLearningJourney.Move(stages[index].Id, 1));
        }

        Assert.Same(stages[0], AgentLearningJourney.Move(stages[^1].Id, int.MinValue));
        Assert.Same(stages[^1], AgentLearningJourney.Move(stages[^1].Id, int.MaxValue));
    }

    /// <summary>Client remains a readable Learn topic even though it is outside the host anatomy.</summary>
    [Fact]
    public void ClientConcept_RemainsAvailableOutsideHostAnatomy()
    {
        var catalog = new ConceptCatalog(new ContentEnvironment(), NullLogger<ConceptCatalog>.Instance);
        var client = catalog.Get("client");
        Assert.NotNull(client);
        Assert.Equal("Client", client.Title);
        Assert.Contains(client, catalog.All);
        Assert.Contains("outside the harness", client.BodyHtml);
    }

    /// <summary>Any concepts and architecture nodes referenced by a stage must exist.</summary>
    [Fact]
    public void Stages_ReferenceExistingConceptsAndNodes()
    {
        var catalog = new ConceptCatalog(new ContentEnvironment(), NullLogger<ConceptCatalog>.Instance);
        Assert.NotEmpty(catalog.All);
        Assert.Equal(AgentLearningJourney.Nodes.Count, AgentLearningJourney.Nodes.Select(node => node.Id).Distinct().Count());

        foreach (var stage in AgentLearningJourney.Stages)
        {
            Assert.All(stage.ConceptIds, id =>
            {
                var concept = catalog.Get(id);
                Assert.NotNull(concept);
                Assert.False(string.IsNullOrWhiteSpace(concept.BodyHtml));
            });
            Assert.All(stage.HighlightedNodes, id => Assert.Contains(AgentLearningJourney.Nodes, node => node.Id == id));
        }
    }

    /// <summary>Demo actions are limited to the local flow page (optionally deep-linked) and stage URLs remain predictable.</summary>
    [Fact]
    public void DemoActions_OnlyNavigateToExistingLocalPages()
    {
        foreach (var stage in AgentLearningJourney.Stages)
        {
            if (stage.ActionHref is not null)
            {
                Assert.True(stage.ActionHref == "/flow" || stage.ActionHref.StartsWith("/flow?", StringComparison.Ordinal), stage.ActionHref);
                Assert.False(string.IsNullOrWhiteSpace(stage.ActionLabel));
            }

            Assert.Equal($"/learn?stage={stage.Id}", AgentLearningJourney.Href(stage.Id));
        }
    }

    /// <summary>Only the tool-calling lesson pre-selects the Wikipedia agent, technical view, disabled tools and a question; nothing is auto-sent.</summary>
    [Fact]
    public void ToolCallingStage_DeepLinksToPreparedFlow()
    {
        var stage = AgentLearningJourney.Resolve("tool-calling");

        Assert.Equal("Show in live flow", stage.ActionLabel);
        Assert.StartsWith("/flow?", stage.ActionHref);
        Assert.Contains("agent=WikiAssistant", stage.ActionHref);
        Assert.Contains("view=technical", stage.ActionHref);
        Assert.Contains("tools=-SearchWiki%2C-GetWikiPage", stage.ActionHref);
        Assert.Contains("prompt=Who%20is%20the%20king%20of%20Norway", stage.ActionHref);
        Assert.DoesNotContain("host=", stage.ActionHref);
        Assert.Equal("/flow", AgentLearningJourney.Resolve("agent-loop").ActionHref);
        Assert.Equal("/flow", AgentLearningJourney.Resolve("run-and-improve").ActionHref);
    }

    /// <summary>The security lesson deep-links to the Security Lab's exfiltration scenario with a before-tool breakpoint; nothing is auto-sent.</summary>
    [Fact]
    public void SecurityStage_DeepLinksToScriptedScenario()
    {
        var stage = AgentLearningJourney.Resolve("what-can-go-wrong");

        Assert.Equal("Show in live flow", stage.ActionLabel);
        Assert.StartsWith("/flow?", stage.ActionHref);
        Assert.Contains("host=security", stage.ActionHref);
        Assert.Contains("agent=LeakyAssistant", stage.ActionHref);
        Assert.Contains("view=technical", stage.ActionHref);
        Assert.Contains("breakpoints=before-tool", stage.ActionHref);
        Assert.Contains("prompt=", stage.ActionHref);
        Assert.Equal(["attack-injection", "attack-exfiltration", "attack-poisoning"], stage.HighlightedNodes.ToArray());
        Assert.Equal("where-to-run", stage.ParentId);
    }

    /// <summary>The loop lesson distinguishes decisions, execution, observations and the answer exit.</summary>
    [Fact]
    public void LoopStage_FocusesOnExecutionCycle()
    {
        var stage = AgentLearningJourney.Resolve("agent-loop");

        Assert.Equal(["loop-context", "loop-decision", "loop-execute", "loop-answer"], stage.HighlightedNodes.ToArray());
        Assert.Equal(["reasoning", "tools", "context"], stage.ConceptIds.ToArray());
        Assert.Equal("/flow", stage.ActionHref);
        Assert.False(stage.PlatformMap);
    }

    /// <summary>The introductory lesson uses its concise title while preserving existing bookmarks.</summary>
    [Fact]
    public void AgentStage_PreservesPermalinkWithConciseTitle()
    {
        Assert.Equal("Agent", AgentLearningJourney.Resolve("model-to-agent").Title);
        Assert.Equal("/learn?stage=model-to-agent", AgentLearningJourney.Href(AgentLearningJourney.Resolve("model-to-agent").Id));
    }

    /// <summary>Navigation skips hidden content and numbers the final visible stage consecutively.</summary>
    [Fact]
    public void Navigation_SkipsHiddenFoundryStage()
    {
        Assert.Equal("what-can-go-wrong", AgentLearningJourney.Move("where-to-run", 1).Id);
        Assert.Equal("what-can-go-wrong", AgentLearningJourney.Move("run-and-improve", -1).Id);
        Assert.Equal(11, AgentLearningJourney.IndexOf("run-and-improve"));
        Assert.All(AgentLearningJourney.Stages, stage => Assert.False(stage.Hidden));
    }

    /// <summary>Each visible lesson selects only its own nodes and is not a product-specific platform mapping.</summary>
    [Theory]
    [InlineData("agent-landscape", "harness,model")]
    [InlineData("agents-everywhere", "harness,model")]
    [InlineData("model-to-agent", "harness,model")]
    [InlineData("tool-calling", "model,harness,execution-controls")]
    [InlineData("inside-the-harness", "context-management,instructions,harness-context,context-history,context-results,available-tools")]
    [InlineData("anatomy-of-agent", "instructions,available-tools,anatomy-persona,anatomy-selected-tools,anatomy-settings,execution-controls,anatomy-task,anatomy-custom-instructions,anatomy-skills")]
    [InlineData("agent-loop", "loop-context,loop-decision,loop-execute,loop-answer")]
    [InlineData("wider-ecosystem", "connected-agent,mcp-tools,a2a-agent")]
    [InlineData("where-to-run", "hosting-runtime,hosting-model,hosting-access,hosting-owner")]
    [InlineData("what-can-go-wrong", "attack-injection,attack-exfiltration,attack-poisoning")]
    [InlineData("run-and-improve", "lifecycle-run,lifecycle-observe,lifecycle-evaluate,lifecycle-improve")]
    public void Stage_SelectsOnlyItsFocusedNodes(string stageId, string expectedNodeIds)
    {
        var expectedNodes = expectedNodeIds.Split(',');
        var stage = AgentLearningJourney.Resolve(stageId);
        Assert.Equal(expectedNodes, stage.HighlightedNodes.ToArray());
        Assert.False(stage.PlatformMap);
        Assert.InRange(stage.ConceptIds.Count, 1, 4);
    }

    /// <summary>Connections inform hosting choices before the final operating cycle, with stable permalinks.</summary>
    [Fact]
    public void HostingStage_HasStablePlacementAndPermalink()
    {
        Assert.Equal("agent-landscape", AgentLearningJourney.Move("agents-everywhere", 1).Id);
        Assert.Equal("agent-landscape", AgentLearningJourney.Move("wider-ecosystem", -1).Id);
        Assert.Equal("where-to-run", AgentLearningJourney.Move("wider-ecosystem", 1).Id);
        Assert.Equal("wider-ecosystem", AgentLearningJourney.Move("where-to-run", -1).Id);
        Assert.Equal("what-can-go-wrong", AgentLearningJourney.Move("where-to-run", 1).Id);
        Assert.Equal("/learn?stage=where-to-run", AgentLearningJourney.Href(AgentLearningJourney.Resolve("WHERE-TO-RUN").Id));
        Assert.Null(AgentLearningJourney.Resolve("where-to-run").ActionHref);
    }

    /// <summary>Vendor examples have valid category mappings and HTTPS documentation, distinct from runtime choices.</summary>
    [Fact]
    public void HostingExamples_MapToOperatingModelsWithSources()
    {
        Assert.Equal(HostingStory.Examples.Count, HostingStory.Examples.Select(example => example.Name).Distinct().Count());
        Assert.All(HostingStory.Examples, example =>
        {
            Assert.All(new[] { example.Name, example.Vendor, example.Kind, example.Detail }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.Equal(Uri.UriSchemeHttps, new Uri(example.Url).Scheme);
            Assert.NotEmpty(example.OptionIds);
            Assert.All(example.OptionIds, id => Assert.Contains(HostingStory.Options, option => option.Id == id));
        });
        var story = new HostingStory();
        story.Complete();
        foreach (var option in HostingStory.Options)
        {
            story.SelectOption(option.Id);
            Assert.NotEmpty(story.CurrentExamples);
            Assert.All(story.CurrentExamples, example => Assert.Contains(option.Id, example.OptionIds));
            Assert.Equal(2, story.Beat);
        }
        Assert.Equal(["local", "service"], HostingStory.Examples.Single(example => example.Name == "Microsoft Agent Framework").OptionIds.ToArray());
        Assert.Equal(["managed"], HostingStory.Examples.Single(example => example.Name == "Microsoft Foundry Agent Service").OptionIds.ToArray());
        Assert.Equal(["product"], HostingStory.Examples.Single(example => example.Name == "Microsoft 365 Copilot").OptionIds.ToArray());
        Assert.Equal(["local", "service"], HostingStory.Examples.Single(example => example.Name == "n8n (self-hosted)").OptionIds.ToArray());
        Assert.Equal(["product"], HostingStory.Examples.Single(example => example.Name == "n8n Cloud").OptionIds.ToArray());
        Assert.Equal(["service", "managed"], HostingStory.Examples.Single(example => example.Name == "LangSmith Deployment").OptionIds.ToArray());
        foreach (var vendor in new[] { "Microsoft", "OpenAI", "Anthropic", "Google", "AWS", "LangChain", "n8n" })
        {
            Assert.Contains(HostingStory.Examples, example => example.Vendor == vendor);
        }
    }

    /// <summary>Hosting choices, triggers and readiness do not change each other or reveal progress.</summary>
    [Fact]
    public void HostingStory_ChoicesAreIndependentAndComplete()
    {
        var story = new HostingStory();
        Assert.Equal(["local", "service", "managed", "product"], HostingStory.Options.Select(option => option.Id).ToArray());
        story.Complete();
        foreach (var option in HostingStory.Options)
        {
            story.SelectOption(option.Id);
            Assert.Same(option, story.Option);
            Assert.All(new[] { option.Fit, option.Runtime, option.Model, option.Access, option.Owner, option.Tradeoff, option.Availability },
                value => Assert.False(string.IsNullOrWhiteSpace(value)));
            foreach (var readiness in HostingStory.ReadinessLevels)
            {
                story.SelectReadiness(readiness.Id);
                Assert.Same(readiness, story.Readiness);
                Assert.All(new[] { readiness.Identity, readiness.State, readiness.Reliability, readiness.Control },
                    value => Assert.False(string.IsNullOrWhiteSpace(value)));
                foreach (var trigger in HostingStory.Triggers)
                {
                    story.SelectTrigger(trigger);
                    Assert.Equal(trigger, story.Trigger);
                    Assert.NotEmpty(story.TriggerDetail);
                    Assert.Same(option, story.Option);
                    Assert.Same(readiness, story.Readiness);
                    Assert.False(story.CanNext);
                }
            }
        }
        var selectedOption = story.Option;
        story.SelectOption("unknown");
        story.SelectReadiness(null);
        story.SelectTrigger("unknown");
        Assert.Same(selectedOption, story.Option);
        Assert.Equal("operate", story.Readiness.Id);
        Assert.Equal("Event", story.Trigger);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.Same(selectedOption, story.Option);
        story.Move(int.MaxValue);
        Assert.Equal(2, story.Beat);
        story.Move(-1);
        Assert.Equal(1, story.Beat);
        story.Move(int.MinValue);
        Assert.False(story.CanPrevious);
        var fresh = new HostingStory();
        Assert.Equal("local", fresh.Option.Id);
        Assert.Equal("try", fresh.Readiness.Id);
        Assert.Equal("User request", fresh.Trigger);
    }

    /// <summary>Reveal navigation is bounded, reversible and reset only when the lesson changes.</summary>
    [Fact]
    public void FoundationStory_NavigationPreservesAndResetsProgress()
    {
        var story = new FoundationStory();
        foreach (var stageId in new[] { "agent-landscape", "model-to-agent", "inside-the-harness", "agents-everywhere", "anatomy-of-agent" })
        {
            story.SetStage(stageId);
            Assert.Equal(0, story.Beat);
            Assert.False(story.CanPrevious);
            story.Move(int.MaxValue);
            Assert.Equal(story.Captions.Count - 1, story.Beat);
            Assert.False(story.CanNext);
            story.SetStage(stageId);
            Assert.Equal(story.Captions.Count - 1, story.Beat);
            story.Move(-1);
            Assert.Equal(story.Captions.Count > 1, story.CanNext);
            story.Complete();
            Assert.False(story.CanNext);
            story.Move(int.MinValue);
            Assert.Equal(0, story.Beat);
            story.Complete();
            story.Restart();
            Assert.Equal(0, story.Beat);
        }
    }

    /// <summary>The landscape stops at the shared foundation, leaving execution to the dedicated loop lesson.</summary>
    [Fact]
    public void FoundationStory_LandscapeHasOnlyTwoReveals()
    {
        var story = new FoundationStory();
        story.SetStage("agent-landscape");

        Assert.Equal(2, story.Captions.Count);
        Assert.Equal(0, story.Beat);
        story.Move(1);
        Assert.Contains("Agent = Harness + Model", story.Caption);
        Assert.False(story.CanNext);
        story.Complete();
        Assert.Equal(1, story.Beat);
        story.Move(1);
        Assert.Equal(1, story.Beat);
        story.Restart();
        Assert.Equal(0, story.Beat);
    }

    /// <summary>The Agent overview has one view without reveal progression.</summary>
    [Fact]
    public void FoundationStory_AgentHasSingleOverview()
    {
        var story = new FoundationStory();
        story.SetStage("model-to-agent");

        Assert.Single(story.Captions);
        Assert.Contains("Agent = Harness + Model", story.Caption);
        Assert.Equal(0, story.Beat);
        Assert.False(story.CanPrevious);
        Assert.False(story.CanNext);
        story.Move(1);
        Assert.Equal(0, story.Beat);
        story.Complete();
        Assert.Equal(0, story.Beat);
        Assert.False(story.CanPrevious);
        Assert.False(story.CanNext);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.False(story.CanPrevious);
        Assert.False(story.CanNext);
    }

    /// <summary>Article content reaches the model only after a matching tool result is supplied.</summary>
    [Fact]
    public void ToolCallingStory_SuppliesWikipediaResultOnlyInNextRequest()
    {
        Assert.Equal("GetWikiPage", ToolCallingStory.Tool.Name);
        Assert.Equal("title", Assert.Single(ToolCallingStory.Tool.Arguments).Name);
        Assert.Equal("Alan Turing", ToolCallingStory.Call.Arguments["title"]);
        Assert.DoesNotContain(ToolCallingStory.FirstRequest.Messages, message => message.Text.Contains(ToolCallingStory.ArticleContent));
        Assert.All(ToolCallingStory.Steps.Take(3), step => Assert.DoesNotContain(ToolCallingStory.ArticleContent, step.Evidence));
        Assert.Equal("result", Assert.Single(ToolCallingStory.Steps, step => step.ExecutesTool).Id);
        var result = Assert.Single(ToolCallingStory.NextRequest.Messages, message => message.Role == "tool");
        Assert.Equal(ToolCallingStory.Call.Id, result.ToolCallId);
        Assert.Equal(ToolCallingStory.ArticleContent, result.Text);
        Assert.Equal(ToolCallingStory.Call, Assert.Single(ToolCallingStory.NextRequest.Messages, message => message.ToolCall is not null).ToolCall);
        Assert.Same(ToolCallingStory.FirstRequest, ToolCallingStory.Steps[0].Request);
        Assert.Same(ToolCallingStory.NextRequest, ToolCallingStory.Steps[4].Request);
        Assert.Contains("https://en.wikipedia.org/wiki/Alan_Turing", ToolCallingStory.Answer);
    }

    /// <summary>Tool calling has six manual steps, bounded navigation and a restart without external activity.</summary>
    [Fact]
    public void ToolCallingStory_NavigationIsBoundedAndRestarts()
    {
        var story = new ToolCallingStory();
        Assert.Equal(6, ToolCallingStory.Steps.Count);
        Assert.False(story.CanPrevious);
        Assert.Equal("description", story.Step.Id);
        story.Move(1);
        Assert.Equal("tool-request", story.Step.Id);
        story.Move(int.MaxValue);
        Assert.False(story.CanNext);
        Assert.Equal("answer", story.Step.Id);
        story.Move(int.MinValue);
        Assert.Equal(0, story.Beat);
        story.Complete();
        Assert.Equal(5, story.Beat);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.True(story.CanNext);
    }

    /// <summary>The cycle returns through the model and harness before answering, with bounded navigation.</summary>
    [Fact]
    public void AgentLoopStory_ExplainsReturnAndExitWithoutExamples()
    {
        var story = new AgentLoopStory();
        Assert.Equal(["context", "model", "execute", "execute", "context", "model", "execute", "answer"], AgentLoopStory.Steps.Select(step => step.Id).ToArray());
        Assert.False(story.CanPrevious);
        story.Move(4);
        Assert.Equal("context", story.Step.Id);
        story.Move(1);
        Assert.Equal("model", story.Step.Id);
        story.Move(1);
        Assert.Equal("execute", story.Step.Id);
        story.Move(1);
        Assert.Equal("answer", story.Step.Id);
        story.Complete();
        Assert.True(story.ShowAll);
        Assert.False(story.CanNext);
        story.Move(int.MinValue);
        Assert.False(story.ShowAll);
        Assert.Equal(0, story.Beat);
        story.Move(int.MaxValue);
        Assert.Equal(7, story.Beat);
        Assert.False(story.ShowAll);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.All(AgentLoopStory.Steps, step =>
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Detail));
            Assert.DoesNotContain("meeting-notes", step.Detail);
            Assert.DoesNotContain("GetWikiPage", step.Detail);
        });
    }

    /// <summary>Context management reveals one selected input at a time and keeps earlier inputs visible.</summary>
    [Fact]
    public void FoundationStory_ContextAddsOnePartPerReveal()
    {
        var story = new FoundationStory();
        story.SetStage("inside-the-harness");
        string[] expectedIds = ["instructions", "available-tools", "harness-context", "context-history", "context-results"];
        Assert.Equal(6, story.Captions.Count);
        Assert.Equal(FoundationStory.ContextManagementSummary, story.Caption);
        Assert.Empty(story.VisibleContextAspects);
        for (var index = 0; index < expectedIds.Length; index++)
        {
            story.Move(1);
            var visibleParts = story.VisibleContextAspects.ToArray();
            Assert.Equal(index + 1, story.Beat);
            Assert.Equal(expectedIds.Take(index + 1), visibleParts.Select(part => part.Id));
            Assert.Equal(visibleParts[^1].Detail, story.Caption);
        }
        Assert.False(story.CanNext);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.Empty(story.VisibleContextAspects);
    }

    /// <summary>Task and trigger selection are independent of presentation progress and each other.</summary>
    [Fact]
    public void FoundationStory_ExamplesAndTriggersAreIndependent()
    {
        var story = new FoundationStory();
        story.SetStage("agents-everywhere");
        story.Complete();
        foreach (var example in FoundationStory.Examples)
        {
            story.SelectExample(example.Id);
            Assert.Same(example, story.Example);
            Assert.All(new[] { example.Task, example.Context, example.LocalTools, example.CloudTools,
                example.LocalControl, example.CloudControl, example.ApplicationExamples }, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            foreach (var trigger in FoundationStory.Triggers)
            {
                story.SelectTrigger(trigger);
                Assert.Equal(trigger, story.Trigger);
                Assert.Same(example, story.Example);
                Assert.False(story.CanNext);
            }
        }
        var selected = story.Example;
        story.SelectExample("unknown");
        story.SelectTrigger("unknown");
        Assert.Same(selected, story.Example);
        Assert.Equal("Event", story.Trigger);
    }

    /// <summary>Anatomy remains addressable after the loop and before the settings comparison.</summary>
    [Fact]
    public void AnatomyStage_HasStablePlacementAndPermalink()
    {
        Assert.Equal("anatomy-of-agent", AgentLearningJourney.Move("agent-loop", 1).Id);
        Assert.Equal("agents-everywhere", AgentLearningJourney.Move("anatomy-of-agent", 1).Id);
        Assert.Equal("agent-loop", AgentLearningJourney.Move("anatomy-of-agent", -1).Id);
        Assert.Equal("/learn?stage=anatomy-of-agent", AgentLearningJourney.Href(AgentLearningJourney.Resolve("ANATOMY-OF-AGENT").Id));
    }

    /// <summary>Example selection preserves reveals while chapter reentry restores the initial example.</summary>
    [Fact]
    public void AnatomyStory_SeparatesSelectionFromRevealProgress()
    {
        var story = new FoundationStory();
        story.SetStage("anatomy-of-agent");
        Assert.Equal(10, story.Captions.Count);
        Assert.Equal("chat", story.AnatomyProfile.Id);
        Assert.Equal("chat-agent", story.Anatomy.Id);
        Assert.Contains(story.LoadedSkill, story.Anatomy.Skills);
        story.SelectAnatomyPurpose("coding");
        Assert.Equal(10, story.Captions.Count);
        Assert.Equal("ask", story.Anatomy.Id);
        story.Move(2);
        foreach (var example in FoundationStory.AnatomyExamples)
        {
            story.SelectAnatomy(example.Id);
            Assert.Same(example, story.Anatomy);
            Assert.Equal(2, story.Beat);
            Assert.Contains(story.LoadedSkill, example.Skills);
        }
        story.SelectAnatomy("unknown");
        Assert.Equal("review", story.Anatomy.Id);
        story.Complete();
        Assert.Equal(9, story.Beat);
        Assert.Contains("Read skill", story.Caption);
        story.SetStage("anatomy-of-agent");
        Assert.Equal(9, story.Beat);
        Assert.Equal("review", story.Anatomy.Id);
        story.Move(-1);
        Assert.Equal(8, story.Beat);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.Equal("review", story.Anatomy.Id);
        story.SetStage("agent-loop");
        story.SetStage("anatomy-of-agent");
        Assert.Equal(0, story.Beat);
        Assert.Equal("chat-agent", story.Anatomy.Id);
    }

    /// <summary>Tool selection, model choice and executable controls are distinct; shorter configurations remain in bounds.</summary>
    [Fact]
    public void AnatomyStory_SeparatesControlsFromModelAndToolSelection()
    {
        var story = new FoundationStory();
        story.SetStage("anatomy-of-agent");
        story.Move(3);
        Assert.Contains("Selected tools", story.Caption);
        story.Move(1);
        Assert.Contains("Model choice", story.Caption);
        story.Move(1);
        Assert.Contains("checks run by the harness", story.Caption);
        Assert.Contains("No publishing", story.AnatomyProfile.Controls);
        story.SelectAnatomyPurpose("coding");
        story.Complete();
        Assert.Equal(9, story.Beat);
        story.SelectAnatomyPurpose("chat");
        Assert.Equal(9, story.Beat);
        Assert.False(story.CanNext);
        Assert.NotNull(story.LoadedSkill);
        story.Restart();
        Assert.Equal("chat", story.AnatomyProfile.Id);
        Assert.Equal(0, story.Beat);
    }

    /// <summary>Illustrative subsets stay bounded; only Implement permits edits and terminal use.</summary>
    [Fact]
    public void AnatomyExamples_UseBoundedCapabilitiesAndCompletePlaybooks()
    {
        var capabilityIds = FoundationStory.AnatomyCapabilities.Select(tool => tool.Id).ToArray();
        Assert.Equal(capabilityIds.Length, capabilityIds.Distinct().Count());
        Assert.Equal(["ask", "plan", "implement", "review"], FoundationStory.AnatomyExamples.Select(example => example.Id).ToArray());
        foreach (var example in FoundationStory.AnatomyExamples)
        {
            Assert.All(example.ToolIds, id => Assert.Contains(id, capabilityIds));
            Assert.Contains("skill", example.ToolIds);
            if (example.Id == "implement")
            {
                Assert.Contains("write", example.ToolIds);
                Assert.Contains("terminal", example.ToolIds);
                Assert.Contains("approval", example.Controls);
                Assert.Contains("limits", example.Controls);
            }
            else
            {
                Assert.DoesNotContain("write", example.ToolIds);
                Assert.DoesNotContain("terminal", example.ToolIds);
                Assert.Null(example.Controls);
            }
            Assert.DoesNotContain("delegate", example.ToolIds);
            Assert.False(string.IsNullOrWhiteSpace(example.Persona));
            Assert.False(string.IsNullOrWhiteSpace(example.Task));
            Assert.Equal(2, example.Skills.Count);
            Assert.All(example.Skills, skill =>
            {
                Assert.False(string.IsNullOrWhiteSpace(skill.Name));
                Assert.False(string.IsNullOrWhiteSpace(skill.Description));
                Assert.Equal(3, skill.Steps.Count);
                Assert.All(skill.Steps, step => Assert.False(string.IsNullOrWhiteSpace(step)));
            });
        }
        Assert.Contains("ask", FoundationStory.AnatomyExamples[1].ToolIds);
        Assert.Contains("docs", FoundationStory.AnatomyExamples.Single(example => example.Id == "review").ToolIds);
        Assert.DoesNotContain("docs", FoundationStory.AnatomyExamples[0].ToolIds);
    }

    /// <summary>Purpose selection changes every anatomy layer without revealing later content.</summary>
    [Fact]
    public void AnatomyPurposes_ProvideCoherentConfigurationsWithoutResettingReveals()
    {
        var story = new FoundationStory();
        story.SetStage("anatomy-of-agent");
        story.Move(2);
        Assert.Equal(["chat", "office", "coding", "custom"], FoundationStory.AnatomyPurposes.Select(profile => profile.Id).ToArray());
        foreach (var profile in FoundationStory.AnatomyPurposes)
        {
            story.SelectAnatomyPurpose(profile.Id);
            Assert.Same(profile, story.AnatomyProfile);
            Assert.Same(profile.Examples[0], story.Anatomy);
            Assert.Equal(2, story.Beat);
            Assert.All(new[] { profile.SystemPrompt, profile.Controls, profile.Instructions, profile.ContextLabel },
                value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.StartsWith("You are ", profile.SystemPrompt);
            Assert.InRange(profile.SystemPrompt.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length, 1, 60);
            var ids = profile.Capabilities.Select(tool => tool.Id).ToArray();
            Assert.Equal(ids.Length, ids.Distinct().Count());
            foreach (var example in profile.Examples)
            {
                story.SelectAnatomy(example.Id);
                Assert.Same(example, story.Anatomy);
                Assert.False(string.IsNullOrWhiteSpace(story.Anatomy.Model));
                Assert.False(string.IsNullOrWhiteSpace(story.Anatomy.ModelReason));
                Assert.All(example.ToolIds, id => Assert.Contains(id, ids));
                Assert.Contains("skill", example.ToolIds);
                Assert.Contains(story.LoadedSkill, example.Skills);
                Assert.Equal(2, example.Skills.Count);
                Assert.All(example.Skills, skill => Assert.Equal(3, skill.Steps.Count));
                Assert.Equal(2, story.Beat);
            }
        }
        story.SelectAnatomyPurpose("office");
        Assert.Contains("send", story.Anatomy.ToolIds);
        Assert.Contains("confirmation", story.AnatomyProfile.Controls);
        Assert.DoesNotContain("delete", story.Anatomy.ToolIds);
        story.SelectAnatomy("review");
        story.SelectAnatomyPurpose("unknown");
        Assert.Equal("office-agent", story.Anatomy.Id);
        story.Complete();
        story.Restart();
        Assert.Equal("office", story.AnatomyProfile.Id);
        Assert.Equal(0, story.Beat);
        story.SetStage("agents-everywhere");
        story.SetStage("anatomy-of-agent");
        Assert.Equal("chat", story.AnatomyProfile.Id);
        Assert.Equal("chat-agent", story.Anatomy.Id);
        story.SelectAnatomyPurpose("custom");
        Assert.DoesNotContain("control", story.Anatomy.ToolIds);
        Assert.DoesNotContain("delegate", story.Anatomy.ToolIds);
    }

    /// <summary>Office personas share their host but differ in model fit, capabilities and task without resetting reveals.</summary>
    [Fact]
    public void OfficePersonas_SeparateMeetingsFromReadOnlyDocumentReview()
    {
        var story = new FoundationStory();
        story.SetStage("anatomy-of-agent");
        story.SelectAnatomyPurpose("office");
        story.Complete();
        var meeting = story.Anatomy;
        var profile = story.AnatomyProfile;
        Assert.Equal(["office-agent", "document-reviewer"], profile.Examples.Select(example => example.Id).ToArray());

        story.SelectAnatomy("document-reviewer");
        Assert.Same(profile, story.AnatomyProfile);
        Assert.Equal(story.Captions.Count - 1, story.Beat);
        Assert.Equal(["documents", "skill"], story.Anatomy.ToolIds.ToArray());
        Assert.Contains("Read-only", story.Anatomy.Controls);
        Assert.NotEqual(meeting.Model, story.Anatomy.Model);
        Assert.NotEqual(meeting.Task, story.Anatomy.Task);
        Assert.Equal("compare-documents", Assert.IsType<AnatomySkill>(story.LoadedSkill).Name);
        story.Restart();
        Assert.Equal("document-reviewer", story.Anatomy.Id);
        story.SelectAnatomy("office-agent");
        Assert.Same(meeting, story.Anatomy);
        Assert.Contains("send", story.Anatomy.ToolIds);
        Assert.Null(story.Anatomy.Controls);
    }

    private sealed class ContentEnvironment : IWebHostEnvironment
    {
        public string ApplicationName { get; set; } = "AgenticLab.Web.Tests";
        public string EnvironmentName { get; set; } = "Development";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string WebRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    }
}