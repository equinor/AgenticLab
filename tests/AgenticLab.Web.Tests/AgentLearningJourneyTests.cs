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
    /// <summary>The second request returns to the same model node; alternate outcomes do not invent tool results.</summary>
    [Fact]
    public void TurnExample_MapsStepsOntoPersistentLoopNodes()
    {
        var story = new AgentTurnStory();
        Assert.Equal("context", story.Step.LoopNodeId);
        Assert.Equal(
            ["context", "model", "model", "execute", "observation", "model", "answer", "answer"],
            story.Scenario.Steps.Select(step => step.LoopNodeId).ToArray());
        foreach (var scenario in AgentTurnStory.Scenarios)
        {
            Assert.All(scenario.Steps, step => Assert.Contains(step.LoopNodeId,
                new[] { "context", "model", "execute", "observation", "answer" }));
            if (scenario.Id is "cancelled" or "limit" or "provided")
                Assert.DoesNotContain(scenario.Steps, step => step.LoopNodeId == "observation");
            if (scenario.Id is "cancelled" or "limit")
                Assert.Equal("execute", scenario.Steps[^1].LoopNodeId);
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
        Assert.Contains("supplies input", stage.Summary);
        Assert.Contains("permitted tool requests", stage.Summary);
        Assert.Equal("Harness", AgentLearningJourney.Node("harness").Title);
        Assert.Equal("Inside the harness", AgentLearningJourney.Resolve("inside-the-harness").Title);
        Assert.Equal("Harness executes", AgentLearningJourney.Node("loop-execute").Title);
        Assert.Contains("Generates responses", AgentLearningJourney.Node("model").Detail);
    }

    /// <summary>Visible stages retain their identifiers and exclude the product-specific lesson.</summary>
    [Fact]
    public void Stages_PreserveOrderedPermalinks()
    {
        Assert.Equal(
                ["why-agents", "model-to-agent", "inside-the-harness", "agent-loop", "anatomy-of-agent", "agents-everywhere", "agent-landscape", "wider-ecosystem", "where-to-run", "run-and-improve"],
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
            ["inside-the-harness", "agent-loop", "anatomy-of-agent"],
            AgentLearningJourney.Children("model-to-agent").Select(stage => stage.Id).ToArray());
        Assert.Equal(
            ["agent-landscape", "wider-ecosystem"],
            AgentLearningJourney.Children("agents-everywhere").Select(stage => stage.Id).ToArray());
        Assert.Equal("run-and-improve", Assert.Single(AgentLearningJourney.Children("where-to-run")).Id);
        Assert.Empty(AgentLearningJourney.Children("why-agents"));
        Assert.Equal(10, AgentLearningJourney.Stages.Count);
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
    [InlineData("agent-loop", "2.2")]
    [InlineData("anatomy-of-agent", "2.3")]
    [InlineData("agents-everywhere", "3")]
    [InlineData("agent-landscape", "3.1")]
    [InlineData("wider-ecosystem", "3.2")]
    [InlineData("where-to-run", "4")]
    [InlineData("run-and-improve", "4.1")]
    [InlineData("AGENT-LOOP", "2.2")]
    [InlineData("map-to-foundry", "1")]
    [InlineData(null, "1")]
    public void Hierarchy_NumbersRootsAndSubtopics(string? stageId, string expected)
    {
        Assert.Equal(expected, AgentLearningJourney.Number(stageId));
    }

    /// <summary>The introduction defines an agent and its bounded autonomy before explaining its parts and operation.</summary>
    [Fact]
    public void Introduction_SetsTheSceneBeforeAgent()
    {
        var introduction = AgentLearningJourney.Resolve(null);
        Assert.Equal("why-agents", introduction.Id);
        Assert.Equal("Intro", introduction.Title);
        Assert.Empty(introduction.HighlightedNodes);
        Assert.Equal(
            ["Purpose", "What does this mean? / What makes this possible?"],
            AgentLearningJourney.IntroductionSteps.Select(step => step.Title).ToArray());
        var purpose = AgentLearningJourney.IntroductionSteps[0];
        Assert.Equal("purpose", purpose.Id);
        Assert.Contains("works toward a goal on your behalf", purpose.Detail);
        Assert.Contains("choosing its next steps", purpose.Detail);
        Assert.Contains("adjusting to results", purpose.Detail);
        Assert.Contains("within the permissions and limits", purpose.Detail);
        Assert.Contains("model", AgentLearningJourney.IntroductionSteps[1].Detail);
        var copy = $"{introduction.Summary} {introduction.Takeaway} {string.Join(' ', AgentLearningJourney.IntroductionSteps.Select(step => step.Detail))}";
        Assert.True(copy.Split(' ').Length <= 100);
        Assert.Equal("model-to-agent", AgentLearningJourney.Move(introduction.Id, 1).Id);
        Assert.Same(introduction, AgentLearningJourney.Move("model-to-agent", -1));
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

    /// <summary>Every stage links to readable concepts and defined architecture nodes.</summary>
    [Fact]
    public void Stages_ReferenceExistingConceptsAndNodes()
    {
        var catalog = new ConceptCatalog(new ContentEnvironment(), NullLogger<ConceptCatalog>.Instance);
        Assert.NotEmpty(catalog.All);
        Assert.Equal(AgentLearningJourney.Nodes.Count, AgentLearningJourney.Nodes.Select(node => node.Id).Distinct().Count());

        foreach (var stage in AgentLearningJourney.Stages)
        {
            Assert.NotEmpty(stage.ConceptIds);
            if (stage.Id != "why-agents") Assert.NotEmpty(stage.HighlightedNodes);
            Assert.All(stage.ConceptIds, id =>
            {
                var concept = catalog.Get(id);
                Assert.NotNull(concept);
                Assert.False(string.IsNullOrWhiteSpace(concept.BodyHtml));
            });
            Assert.All(stage.HighlightedNodes, id => Assert.Contains(AgentLearningJourney.Nodes, node => node.Id == id));
        }
    }

    /// <summary>Demo actions are limited to existing local pages and stage URLs remain predictable.</summary>
    [Fact]
    public void DemoActions_OnlyNavigateToExistingLocalPages()
    {
        foreach (var stage in AgentLearningJourney.Stages)
        {
            if (stage.ActionHref is not null)
            {
                Assert.Equal("/", stage.ActionHref);
                Assert.False(string.IsNullOrWhiteSpace(stage.ActionLabel));
            }

            Assert.Equal($"/learn?stage={stage.Id}", AgentLearningJourney.Href(stage.Id));
        }
    }

    /// <summary>The host lesson uses the overview's five responsibilities in the same order and wording.</summary>
    [Fact]
    public void HarnessStage_FocusesOnResponsibilities()
    {
        var stage = AgentLearningJourney.Resolve("inside-the-harness");

        Assert.Equal(["harness-context", "instructions", "available-tools", "harness-memory", "execution-controls"], stage.HighlightedNodes.ToArray());
        Assert.Equal(
            ["Assemble context", "Load instruction text", "Describe tools", "Retain session messages", "Check tool requests"],
            stage.HighlightedNodes.Select(id => AgentLearningJourney.Node(id).Title).ToArray());
        Assert.Equal(stage.HighlightedNodes, AgentLearningJourney.HarnessExamples.Select(example => example.NodeId).ToArray());
        Assert.Equal(stage.HighlightedNodes, AgentTurnStory.Responsibilities.Select(item => item.NodeId).ToArray());
        Assert.All(AgentTurnStory.Responsibilities, item =>
            Assert.All(new[] { item.Input, item.Operation, item.Output, item.Boundary }, text => Assert.False(string.IsNullOrWhiteSpace(text))));
        Assert.All(AgentLearningJourney.HarnessExamples, example => Assert.False(string.IsNullOrWhiteSpace(example.Text)));
        Assert.Equal(["system-prompt", "context", "tools", "guardrails"], stage.ConceptIds.ToArray());
        Assert.False(stage.PlatformMap);
    }

    /// <summary>The loop lesson distinguishes decisions, execution, observations and the answer exit.</summary>
    [Fact]
    public void LoopStage_FocusesOnExecutionCycle()
    {
        var stage = AgentLearningJourney.Resolve("agent-loop");

        Assert.Equal(["loop-context", "loop-decision", "loop-execute", "loop-observe", "loop-answer"], stage.HighlightedNodes.ToArray());
        Assert.Equal(["reasoning", "tools", "context"], stage.ConceptIds.ToArray());
        Assert.Equal("/", stage.ActionHref);
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
        Assert.Equal("run-and-improve", AgentLearningJourney.Move("where-to-run", 1).Id);
        Assert.Equal("where-to-run", AgentLearningJourney.Move("run-and-improve", -1).Id);
        Assert.Equal(9, AgentLearningJourney.IndexOf("run-and-improve"));
        Assert.All(AgentLearningJourney.Stages, stage => Assert.False(stage.Hidden));
    }

    /// <summary>Each visible lesson selects only its own nodes and is not a product-specific platform mapping.</summary>
    [Theory]
    [InlineData("agent-landscape", "harness,model")]
    [InlineData("agents-everywhere", "harness,model")]
    [InlineData("model-to-agent", "harness,model")]
    [InlineData("inside-the-harness", "harness-context,instructions,available-tools,harness-memory,execution-controls")]
    [InlineData("anatomy-of-agent", "instructions,available-tools,anatomy-persona,anatomy-selected-tools,anatomy-settings,anatomy-task,anatomy-custom-instructions,anatomy-skills")]
    [InlineData("agent-loop", "loop-context,loop-decision,loop-execute,loop-observe,loop-answer")]
    [InlineData("wider-ecosystem", "connected-agent,mcp-tools,a2a-agent")]
    [InlineData("where-to-run", "hosting-runtime,hosting-model,hosting-access,hosting-owner")]
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
        Assert.Equal("run-and-improve", AgentLearningJourney.Move("where-to-run", 1).Id);
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
        Assert.Equal(["local", "product", "service", "managed"], HostingStory.Options.Select(option => option.Id).ToArray());
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
        story.SelectOption("unknown");
        story.SelectReadiness(null);
        story.SelectTrigger("unknown");
        Assert.Equal("managed", story.Option.Id);
        Assert.Equal("operate", story.Readiness.Id);
        Assert.Equal("Event", story.Trigger);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.Equal("managed", story.Option.Id);
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
            Assert.True(story.CanNext);
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

    /// <summary>Composition separates the outbound model request from its response before revealing the agent boundary.</summary>
    [Fact]
    public void FoundationStory_CompositionSeparatesRequestAndResponse()
    {
        var story = new FoundationStory();
        story.SetStage("model-to-agent");

        Assert.Equal(7, story.Captions.Count);
        Assert.Contains("Agent = Harness + Model", story.Caption);
        story.Move(1);
        Assert.Contains("selects instruction text and messages", story.Caption);
        Assert.Contains("execution controls", story.Caption);
        story.Move(1);
        Assert.Contains("The harness executes tools", story.Caption);
        Assert.Contains("meeting-notes.txt", story.Caption);
        story.Move(1);
        Assert.Contains("generates a response", story.Caption);
        Assert.Contains("does not execute tools itself", story.Caption);
        story.Move(1);
        Assert.Contains("selected messages and tool definitions", story.Caption);
        Assert.Contains("does not contain its text", story.Caption);
        story.Move(1);
        Assert.Contains("text, tool requests or both", story.Caption);
        Assert.True(story.CanNext);
        story.Move(1);
        Assert.Contains("does not prove the task succeeded", story.Caption);
        Assert.False(story.CanNext);
    }

    /// <summary>Each harness reveal adds one responsibility while retaining previous nodes.</summary>
    [Fact]
    public void FoundationStory_HarnessAddsOneResponsibilityPerReveal()
    {
        var story = new FoundationStory();
        story.SetStage("inside-the-harness");
        Assert.Equal(AgentTurnStory.Responsibilities.Count, story.Captions.Count);
        for (var index = 0; index < AgentTurnStory.Responsibilities.Count; index++)
        {
            Assert.Equal(index, story.Beat);
            Assert.Equal(AgentTurnStory.Responsibilities[index].Output, story.Caption);
            story.Move(1);
        }
        Assert.False(story.CanNext);
        story.Restart();
        Assert.Equal(0, story.Beat);
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
        Assert.Equal(8, story.Captions.Count);
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
        Assert.Equal(7, story.Beat);
        Assert.Contains("Read skill", story.Caption);
        story.SetStage("anatomy-of-agent");
        Assert.Equal(7, story.Beat);
        Assert.Equal("review", story.Anatomy.Id);
        story.Move(-1);
        Assert.Equal(6, story.Beat);
        story.Restart();
        Assert.Equal(0, story.Beat);
        Assert.Equal("review", story.Anatomy.Id);
        story.SetStage("agent-loop");
        story.SetStage("anatomy-of-agent");
        Assert.Equal(0, story.Beat);
        Assert.Equal("ask", story.Anatomy.Id);
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
        Assert.Equal("coding", story.AnatomyProfile.Id);
        Assert.Equal("ask", story.Anatomy.Id);
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
        Assert.Equal(7, story.Beat);
        Assert.Equal(["documents", "skill"], story.Anatomy.ToolIds.ToArray());
        Assert.Contains("Read-only", story.Anatomy.Controls);
        Assert.NotEqual(meeting.Model, story.Anatomy.Model);
        Assert.NotEqual(meeting.Task, story.Anatomy.Task);
        Assert.Equal("compare-documents", story.LoadedSkill.Name);
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