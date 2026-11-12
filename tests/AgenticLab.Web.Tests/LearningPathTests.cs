using Microsoft.JSInterop;
using AgenticLab.Web.Learning;
using Xunit;

namespace AgenticLab.Web.Tests;

/// <summary>
/// Protects the learning-path catalogue, its URLs and browser-local progress: stable step identifiers,
/// references to existing lessons, and the difference between visiting, completing and skipping a step.
/// </summary>
public sealed class LearningPathTests
{
    private static LearningPath Beginner => LearningPaths.Find("beginner")!;

    /// <summary>Every level has a startable path named by its outcome.</summary>
    [Fact]
    public void Paths_AreNamedByOutcomeAndAllAreStartable()
    {
        Assert.Equal(["beginner", "intermediate", "advanced"], LearningPaths.All.Select(path => path.Id).ToArray());
        Assert.Equal([PathLevel.Beginner, PathLevel.Intermediate, PathLevel.Advanced], LearningPaths.All.Select(path => path.Level).ToArray());
        Assert.Equal(["Understand an agent", "Control a run", "Investigate and extend"], LearningPaths.All.Select(path => path.Title).ToArray());
        Assert.All(LearningPaths.All, path => Assert.False(string.IsNullOrWhiteSpace(path.Outcome)));

        foreach (var path in LearningPaths.All)
        {
            Assert.True(path.Available);
            Assert.Empty(path.Outline);
            Assert.True(path.Steps.Count >= 6);
            Assert.Equal("reflect", path.Steps[^1].Id);
        }

        Assert.Same(Beginner, LearningPaths.Find("BEGINNER"));
        Assert.Null(LearningPaths.Find("expert"));
        Assert.Null(LearningPaths.Find(null));
    }

    /// <summary>Step identifiers are bookmarks, and lessons are reused by reference without touching Anatomy.</summary>
    [Fact]
    public void Beginner_KeepsStableStepIdsAndReusesExistingLessons()
    {
        Assert.Equal(
            ["agent-model", "context", "tools-disabled", "tools-enabled", "agent-loop", "reflect"],
            Beginner.Steps.Select(step => step.Id).ToArray());
        Assert.Equal(["why-agents", "model-to-agent"], Beginner.Find("agent-model")!.StageIds.ToArray());
        Assert.Equal(["inside-the-harness"], Beginner.Find("context")!.StageIds.ToArray());
        Assert.Equal(["agent-loop"], Beginner.Find("AGENT-LOOP")!.StageIds.ToArray());

        var stageIds = AgentLearningJourney.Stages.Select(stage => stage.Id).ToArray();
        foreach (var path in LearningPaths.All)
        {
            Assert.Equal(path.Steps.Count, path.Steps.Select(step => step.Id).Distinct().Count());
            Assert.All(path.Steps.SelectMany(step => step.StageIds), id => Assert.Contains(id, stageIds));
            Assert.DoesNotContain("anatomy-of-agent", path.Steps.SelectMany(step => step.StageIds));
        }
    }

    /// <summary>Every step explains and asks for reflection; a step with something to do also asks for a prediction.</summary>
    [Fact]
    public void Steps_FollowExplainPredictTryReflect()
    {
        foreach (var step in LearningPaths.All.SelectMany(path => path.Steps))
        {
            Assert.False(string.IsNullOrWhiteSpace(step.Explain));
            Assert.NotEmpty(step.Reflect);
            Assert.Equal(step.Try is null, step.Predict is null);
            Assert.Equal(step.Try is not null, step.StageIds.Count > 0 || step.Exercise is not null);
            if (step.Exercise is { } exercise)
            {
                Assert.Empty(step.StageIds);
                Assert.False(string.IsNullOrWhiteSpace(exercise.Setup.Agent));
                Assert.False(string.IsNullOrWhiteSpace(exercise.Setup.Prompt));
                Assert.False(string.IsNullOrWhiteSpace(exercise.Task));
                // The page is already open when the task is read, and it never sends for the learner.
                Assert.DoesNotContain("Open the exercise", exercise.Task);
                Assert.Contains("Press Send", exercise.Task);
                Assert.Null(exercise.Setup.Tour);
                Assert.NotEmpty(exercise.RequiredTools);
                Assert.NotEmpty(exercise.LookFor);
                Assert.True(exercise.Walkthrough.Count >= 3);
            }
        }

        Assert.Equal(["tools-disabled", "tools-enabled"],
            Beginner.Steps.Where(step => step.Exercise is not null).Select(step => step.Id).ToArray());
        Assert.Null(Beginner.Find("reflect")!.Try);
        Assert.Equal(3, Beginner.Find("reflect")!.Reflect.Count);
    }

    /// <summary>The comparison asks the same question of the same agent; only the offered tools differ.</summary>
    [Fact]
    public void Exercises_DifferOnlyInTheOfferedTools()
    {
        var disabled = Beginner.Find("tools-disabled")!.Exercise!;
        var enabled = Beginner.Find("tools-enabled")!.Exercise!;

        Assert.Equal(disabled.Setup with { Tools = null }, enabled.Setup with { Tools = null });
        Assert.NotEqual(disabled.Task, enabled.Task);
        Assert.Equal(["-SearchWiki", "-GetWikiPage"], disabled.Setup.Tools);
        Assert.Equal(["SearchWiki", "GetWikiPage"], enabled.Setup.Tools);
        Assert.Equal(disabled.RequiredTools, enabled.RequiredTools);
        Assert.DoesNotContain(disabled.Walkthrough, beat => beat.Text.Contains("Requests", StringComparison.Ordinal));
        Assert.Contains(enabled.Walkthrough, beat => beat.Actor == "Model" && beat.Text.Contains("request, not an execution", StringComparison.Ordinal));
        // Offering tools permits a call; the step must say that a run without one is a valid outcome.
        Assert.Contains(enabled.LookFor, item => item.Contains("no tool call", StringComparison.Ordinal));
    }

    /// <summary>Path, step, lesson and exercise links are local and built from catalogue identifiers.</summary>
    [Fact]
    public void Urls_UseStableIdentifiers()
    {
        var step = Beginner.Find("tools-disabled")!;
        Assert.Equal("/learn/paths", LearningPaths.Route);
        Assert.Equal("/learn/paths/beginner", LearningPaths.Href(Beginner));
        Assert.Equal("/learn/paths/beginner?step=tools-disabled", LearningPaths.StepHref(Beginner, step));
        Assert.Equal("/learn?stage=model-to-agent&path=beginner&step=agent-model",
            LearningPaths.LessonHref(Beginner, Beginner.Find("agent-model")!, "model-to-agent"));

        var exercise = LearningPaths.ExerciseHref(Beginner, step);
        Assert.StartsWith("/flow?tour=beginner%3Atools-disabled&", exercise);
        Assert.Contains("agent=WikiAssistant", exercise);
        Assert.Contains("tools=-SearchWiki%2C-GetWikiPage", exercise);
        Assert.Contains("prompt=Who%20is%20the%20king%20of%20Norway", exercise);
        Assert.Null(LearningPaths.ExerciseHref(Beginner, Beginner.Find("reflect")!));

        Assert.Equal("/learn/paths/beginner?step=context", LearningPaths.ReturnHref("Beginner", "CONTEXT"));
        Assert.Null(LearningPaths.ReturnHref("beginner", "missing"));
        Assert.Null(LearningPaths.ReturnHref("intermediate", "context"));
        Assert.Null(LearningPaths.ReturnHref(null, null));
    }

    /// <summary>A tour names a catalogue exercise; anything else resolves to nothing.</summary>
    [Theory]
    [InlineData("beginner:tools-disabled", "tools-disabled")]
    [InlineData("BEGINNER:Tools-Enabled", "tools-enabled")]
    [InlineData("beginner:reflect", null)]
    [InlineData("beginner:agent-model", null)]
    [InlineData("beginner:missing", null)]
    [InlineData("intermediate:tools-disabled", null)]
    [InlineData("beginner", null)]
    [InlineData("beginner:tools-disabled:extra", null)]
    [InlineData("https://example.invalid/", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void ResolveTour_AcceptsOnlyCatalogueExercises(string? tour, string? expectedStep)
    {
        var resolved = LearningPaths.ResolveTour(tour);
        Assert.Equal(expectedStep, resolved?.Step.Id);
        if (resolved is { } found)
        {
            Assert.Same(Beginner, found.Path);
        }
    }

    /// <summary>Opening a step is not completing it, and skipping is not completing it either.</summary>
    [Fact]
    public void Progress_SeparatesVisitedCompletedAndSkipped()
    {
        var progress = new LearningProgress();
        var first = Beginner.Steps[0];
        var second = Beginner.Steps[1];
        var third = Beginner.Steps[2];
        Assert.False(progress.HasProgress(Beginner));
        Assert.Null(progress.Resume(Beginner));

        Assert.True(progress.Visit(Beginner, first));
        Assert.False(progress.Visit(Beginner, first));
        Assert.Equal(StepStatus.Visited, progress.Status(Beginner, first));
        Assert.Equal(0, progress.CompletedCount(Beginner));

        progress.Complete(Beginner, first);
        progress.Skip(Beginner, second);
        Assert.Equal(StepStatus.Completed, progress.Status(Beginner, first));
        Assert.Equal(StepStatus.Skipped, progress.Status(Beginner, second));
        Assert.Equal(StepStatus.NotVisited, progress.Status(Beginner, third));
        Assert.Equal(1, progress.CompletedCount(Beginner));

        // Reopening keeps the explicit status and only moves the resume location.
        Assert.True(progress.Visit(Beginner, second));
        Assert.True(progress.Visit(Beginner, first));
        Assert.Equal(StepStatus.Completed, progress.Status(Beginner, first));
        Assert.Equal(StepStatus.Skipped, progress.Status(Beginner, second));
        Assert.Same(first, progress.Resume(Beginner));

        progress.Complete(Beginner, second);
        Assert.Equal(2, progress.CompletedCount(Beginner));
    }

    /// <summary>Stored progress is versioned, holds identifiers only and restores statuses and the resume step.</summary>
    [Fact]
    public void Progress_RoundTripsThroughVersionedJson()
    {
        var progress = new LearningProgress();
        progress.Complete(Beginner, Beginner.Find("agent-model")!);
        progress.Skip(Beginner, Beginner.Find("context")!);
        progress.Visit(Beginner, Beginner.Find("tools-disabled")!);

        var json = progress.Serialize();
        Assert.Equal(
            """{"v":1,"paths":{"beginner":{"resume":"tools-disabled","steps":{"agent-model":"completed","context":"skipped","tools-disabled":"visited"}}}}""",
            json);

        var restored = LearningProgress.Parse(json);
        Assert.Equal(StepStatus.Completed, restored.Status(Beginner, Beginner.Find("agent-model")!));
        Assert.Equal(StepStatus.Skipped, restored.Status(Beginner, Beginner.Find("context")!));
        Assert.Equal(StepStatus.Visited, restored.Status(Beginner, Beginner.Find("tools-disabled")!));
        Assert.Equal(StepStatus.NotVisited, restored.Status(Beginner, Beginner.Find("reflect")!));
        Assert.Equal("tools-disabled", restored.Resume(Beginner)!.Id);
        Assert.Equal(json, restored.Serialize());

        restored.Reset(Beginner);
        Assert.False(restored.HasProgress(Beginner));
        Assert.Equal("""{"v":1,"paths":{}}""", restored.Serialize());
    }

    /// <summary>Unreadable, foreign-version or oversized data starts empty instead of failing.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("""{"v":1}""")]
    [InlineData("""{"v":1,"paths":null}""")]
    [InlineData("""{"v":2,"paths":{"beginner":{"resume":"context","steps":{"context":"completed"}}}}""")]
    [InlineData("""{"v":1,"paths":{"beginner":null}}""")]
    [InlineData("""{"v":1,"paths":{"beginner":{"resume":7}}}""")]
    public void Progress_IgnoresUnusableData(string? stored)
    {
        var progress = LearningProgress.Parse(stored);
        Assert.False(progress.HasProgress(Beginner));
        Assert.Equal(0, progress.CompletedCount(Beginner));

        Assert.False(LearningProgress.Parse(new string(' ', 20_000) + """{"v":1,"paths":{}}""").HasProgress(Beginner));
    }

    /// <summary>Entries for removed paths, removed steps or unknown statuses are dropped; the rest is kept.</summary>
    [Fact]
    public void Progress_DropsStaleEntriesButKeepsValidOnes()
    {
        var progress = LearningProgress.Parse(
            """{"v":1,"paths":{"retired":{"resume":"a","steps":{"a":"completed"}},"beginner":{"resume":"removed-step","steps":{"context":"COMPLETED","removed-step":"completed","reflect":"mastered","agent-loop":"notvisited","agent-model":"7"}}}}""");

        Assert.Equal(StepStatus.Completed, progress.Status(Beginner, Beginner.Find("context")!));
        Assert.Equal(1, progress.CompletedCount(Beginner));
        Assert.Equal(StepStatus.NotVisited, progress.Status(Beginner, Beginner.Find("reflect")!));
        Assert.Equal(StepStatus.NotVisited, progress.Status(Beginner, Beginner.Find("agent-loop")!));
        Assert.Equal(StepStatus.NotVisited, progress.Status(Beginner, Beginner.Find("agent-model")!));
        Assert.Null(progress.Resume(Beginner));
        Assert.Equal("""{"v":1,"paths":{"beginner":{"resume":null,"steps":{"context":"completed"}}}}""", progress.Serialize());
    }

    /// <summary>Paths keep working when the browser's storage cannot be read or written.</summary>
    [Fact]
    public async Task Store_ToleratesUnavailableStorage()
    {
        var failing = new LearningProgressStore(new Storage(fail: true));
        var progress = await failing.LoadAsync();
        Assert.False(progress.HasProgress(Beginner));
        progress.Complete(Beginner, Beginner.Steps[0]);
        await failing.SaveAsync(progress);

        var storage = new Storage(fail: false);
        var store = new LearningProgressStore(storage);
        await store.SaveAsync(progress);
        Assert.Equal(["agenticlab-learning-progress"], storage.Values.Keys.ToArray());
        Assert.Equal(StepStatus.Completed, (await store.LoadAsync()).Status(Beginner, Beginner.Steps[0]));
    }

    /// <summary>New paths have stable bookmarks and keep progress and resets independent of one another.</summary>
    [Fact]
    public void NewPaths_KeepStableStepsAndIndependentProgress()
    {
        var intermediate = LearningPaths.Find("intermediate")!;
        var advanced = LearningPaths.Find("advanced")!;
        Assert.Equal(["tool-selection", "pause-and-step", "replay", "compare-models", "discovery", "workspace-context", "reflect"],
            intermediate.Steps.Select(step => step.Id));
        Assert.Equal(["captured-context", "security-scenario", "defended-boundary", "workspace-agent", "example-module", "hosting", "reflect"],
            advanced.Steps.Select(step => step.Id));

        var progress = new LearningProgress();
        foreach (var path in LearningPaths.All)
        {
            progress.Complete(path, path.Steps[0]);
            progress.Skip(path, path.Steps[1]);
            progress.Visit(path, path.Steps[2]);
        }

        var restored = LearningProgress.Parse(progress.Serialize());
        restored.Reset(intermediate);
        Assert.False(restored.HasProgress(intermediate));
        foreach (var path in new[] { Beginner, advanced })
        {
            Assert.Equal(1, restored.CompletedCount(path));
            Assert.Equal(StepStatus.Skipped, restored.Status(path, path.Steps[1]));
            Assert.Same(path.Steps[2], restored.Resume(path));
        }
    }

    /// <summary>Every exercise resolves from its URL to its own catalogue entry, including the new levels.</summary>
    [Fact]
    public void AllExerciseLinks_ResolveToTheirOwnPathAndStep()
    {
        foreach (var path in LearningPaths.All)
        foreach (var step in path.Steps.Where(step => step.Exercise is not null))
        {
            var href = LearningPaths.ExerciseHref(path, step)!;
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(href[href.IndexOf('?')..]);
            var tour = LearningPaths.ResolveTour(query["tour"]);
            Assert.Same(path, tour!.Value.Path);
            Assert.Same(step, tour.Value.Step);
            Assert.Equal(LearningPaths.StepHref(path, step), LearningPaths.ReturnHref(path.Id, step.Id));
        }
    }

    private sealed class Storage(bool fail) : IJSRuntime
    {
        public Dictionary<string, string> Values { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
        {
            if (fail)
            {
                throw new JSException("Storage is unavailable.");
            }

            var key = (string)args![0]!;
            if (identifier == "localStorage.setItem")
            {
                Values[key] = (string)args[1]!;
                return ValueTask.FromResult(default(TValue)!);
            }

            Assert.Equal("localStorage.getItem", identifier);
            return ValueTask.FromResult((TValue)(object)Values.GetValueOrDefault(key)!);
        }

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args) =>
            InvokeAsync<TValue>(identifier, args);
    }
}
