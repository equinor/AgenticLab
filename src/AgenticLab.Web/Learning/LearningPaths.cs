using AgenticLab.Web.Flow;

namespace AgenticLab.Web.Learning;

/// <summary>The audience a path is written for. A label for choosing, never a lock.</summary>
internal enum PathLevel
{
    Beginner,
    Intermediate,
    Advanced,
}

/// <summary>One line of an illustrated walkthrough: who acts and what happens. Written by hand, not captured from a run.</summary>
internal sealed record WalkthroughBeat(string Actor, string Text);

/// <summary>
/// A live-flow exercise. The catalogue owns the setup, so a <c>tour</c> link only has to name the path and step.
/// <paramref name="Task"/> is the instruction shown beside the prepared page. <paramref name="RequiredTools"/> must
/// exist on the agent before the setup is applied.
/// </summary>
internal sealed record PathExercise(
    FlowLinkRequest Setup,
    string Task,
    IReadOnlyList<string> RequiredTools,
    IReadOnlyList<string> LookFor,
    IReadOnlyList<WalkthroughBeat> Walkthrough);

/// <summary>
/// One stop on a path, following explain, predict, try, reflect. <paramref name="StageIds"/> reference existing
/// agent-guide lessons; <paramref name="Exercise"/> prepares the live flow. Predict and Try are omitted on a
/// reflection-only stop.
/// </summary>
internal sealed record PathStep(
    string Id,
    string Title,
    string Explain,
    string? Predict,
    string? Try,
    IReadOnlyList<string> Reflect,
    IReadOnlyList<string> StageIds,
    PathExercise? Exercise = null);

/// <summary>
/// An outcome-named sequence of steps. A path without steps is planned: only its <paramref name="Outline"/> is shown.
/// </summary>
internal sealed record LearningPath(
    string Id,
    PathLevel Level,
    string Title,
    string Outcome,
    IReadOnlyList<PathStep> Steps,
    IReadOnlyList<string> Outline)
{
    /// <summary>Whether the path can be started.</summary>
    public bool Available => Steps.Count > 0;

    /// <summary>Finds a step by its stable identifier, ignoring case; null when unknown.</summary>
    public PathStep? Find(string? stepId) =>
        Steps.FirstOrDefault(step => string.Equals(step.Id, stepId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The zero-based position of a step on this path.</summary>
    public int IndexOf(PathStep step) => Steps.TakeWhile(candidate => candidate.Id != step.Id).Count();

    /// <summary>The step <paramref name="offset"/> positions away, or null beyond either end.</summary>
    public PathStep? Neighbour(PathStep step, int offset)
    {
        var index = IndexOf(step) + offset;
        return index >= 0 && index < Steps.Count ? Steps[index] : null;
    }
}

/// <summary>
/// The learning-path catalogue and its URLs. Paths reuse agent-guide lessons by stage identifier and prepare
/// live-flow exercises through <see cref="FlowDeepLink"/>; they add no lesson content of their own.
/// </summary>
internal static class LearningPaths
{
    /// <summary>The path picker route; a path lives at <c>/learn/paths/{id}?step={step-id}</c>.</summary>
    public const string Route = "/learn/paths";

    private const string WikiAgent = "WikiAssistant";
    private const string WikiQuestion = "Who is the king of Norway";
    private static readonly string[] WikiTools = ["SearchWiki", "GetWikiPage"];

    internal static IReadOnlyList<LearningPath> All { get; } = Array.AsReadOnly<LearningPath>(
    [
        new("beginner", PathLevel.Beginner, "Understand an agent",
            "Tell the harness from the model, and see what changes when the model is offered tools.",
            [
                new("agent-model", "Agent = Harness + Model",
                    "An agent has two parts. The model generates text and can request actions. The harness is the software around it: it builds the model's input, checks tool requests and runs the permitted ones.",
                    "When a chat application looks something up on the web, which part performs the search: the model or the application?",
                    "Read both lessons. In Intro, step through all four reveals.",
                    ["In your own words: what does the model do, and what does the harness do?"],
                    ["why-agents", "model-to-agent"]),
                new("context", "What the model sees",
                    "The model only sees the input of one request. The harness selects that input: instructions, the task, earlier messages, tool results and tool descriptions. This selection is the context.",
                    "If a tool is not described in the context, can the model request it?",
                    "Read the lesson and reveal each part of the context.",
                    ["Which parts of the context come from the user, and which does the harness add?"],
                    ["inside-the-harness"]),
                new("tools-disabled", "Ask with tools switched off",
                    "The Wikipedia agent normally offers the model two tools. This exercise switches both off for the run, so the harness does not offer them to the model. The model can still answer from what it learned in training.",
                    "Will the model still answer? Will the answer necessarily be wrong?",
                    "Open the exercise. The question is already in the message box: press Send, then read the Execution panel.",
                    ["What in the run shows that no tool was used? Could you tell from the answer text alone?"],
                    [],
                    new(new FlowLinkRequest(Agent: WikiAgent, Prompt: WikiQuestion, View: DiagramPreset.Technical,
                            Tools: ["-SearchWiki", "-GetWikiPage"]),
                        "The question is already in the message box. Press Send, then read the Execution panel.",
                        WikiTools,
                        [
                            "The Execution panel lists a model request and a model response, with no tool call.",
                            "The answer comes from the model alone. It can be correct, outdated or wrong.",
                        ],
                        [
                            new("User", $"Asks: {WikiQuestion}"),
                            new("Harness", "Builds the context. Both tools are switched off, so it offers the model no tools."),
                            new("Model", "Returns answer text. It cannot request a tool it was not offered."),
                            new("Harness", "Returns the answer to the user. The run contains no tool call."),
                        ])),
                new("tools-enabled", "Ask again with tools switched on",
                    "Now the harness offers the model both Wikipedia tools. Offering a tool permits its use. It does not force the model to call it, and it does not guarantee a better answer.",
                    "Will the model request a tool for the same question? What would you expect to see if it does?",
                    "Open the exercise. It starts a new conversation with the same question: press Send, then compare the Execution panel with what you saw when the tools were switched off.",
                    ["What changed between the two runs: the model, the harness, or what the harness offered the model?"],
                    [],
                    new(new FlowLinkRequest(Agent: WikiAgent, Prompt: WikiQuestion, View: DiagramPreset.Technical,
                            Tools: WikiTools),
                        "This is a new conversation with the same question. Press Send, then compare the Execution panel with what you saw when the tools were switched off.",
                        WikiTools,
                        [
                            "If the model requested a tool: a tool call, a tool result, then a second model request that includes the result.",
                            "If there is no tool call, the model chose to answer directly. That is a valid outcome. Send the question again, or read the illustration on the path page.",
                        ],
                        [
                            new("User", $"Asks: {WikiQuestion}"),
                            new("Harness", "Builds the context, including descriptions of SearchWiki and GetWikiPage."),
                            new("Model", "Requests SearchWiki with a query. This is a request, not an execution."),
                            new("Harness", "Checks the request, runs the tool and adds the result to the context."),
                            new("Model", "Receives the result in a second request and returns answer text."),
                            new("Harness", "Returns the answer to the user."),
                        ])),
                new("agent-loop", "Follow the loop",
                    "A tool result is not the answer. The harness adds it to the context and calls the model again. This repeats until the model returns an answer or the harness stops the turn.",
                    "After a tool returns its result, who sees it first: the user or the model?",
                    "Read the lesson and step through its eight steps.",
                    ["In the run with tools switched on, where did the tool result go before the answer appeared?"],
                    ["agent-loop"]),
                new("reflect", "Reflect",
                    "You asked the same question twice. The model was the same both times. What changed was what the harness offered it.",
                    null,
                    null,
                    [
                        "What does the model do, and what does the harness do?",
                        "What changed between the run with tools switched off and the run with tools switched on?",
                        "Why is an answer on its own not evidence that a tool was called?",
                    ],
                    []),
            ],
            []),
        new("intermediate", PathLevel.Intermediate, "Control a run",
            "Decide what a run may use, pause it, and read what it did.",
            [],
            [
                "Switch individual tools on and off for a run",
                "Pause a run with breakpoints and step through it manually",
                "Replay a captured run in the Execution panel",
                "Compare models and read the usage figures",
                "See which MCP tools and A2A agents were discovered",
                "Use workspace skills and custom instructions",
            ]),
        new("advanced", PathLevel.Advanced, "Investigate and extend",
            "Inspect what was sent to the model, see how agents are attacked, and add your own.",
            [],
            [
                "Walk through the Security Lab's scripted attack scenarios",
                "Read captured model requests and the prompt signature",
                "Define an agent in a workspace file",
                "Add an example module",
                "Choose where an agent runs",
            ]),
    ]);

    /// <summary>Finds a path by identifier, ignoring case; null when unknown.</summary>
    internal static LearningPath? Find(string? pathId) =>
        All.FirstOrDefault(path => string.Equals(path.Id, pathId, StringComparison.OrdinalIgnoreCase));

    /// <summary>The path's own URL, which resumes it or opens its first step.</summary>
    internal static string Href(LearningPath path) => $"{Route}/{Uri.EscapeDataString(path.Id)}";

    /// <summary>The bookmarkable URL of one step.</summary>
    internal static string StepHref(LearningPath path, PathStep step) =>
        $"{Href(path)}?step={Uri.EscapeDataString(step.Id)}";

    /// <summary>An agent-guide lesson URL carrying the step to return to.</summary>
    internal static string LessonHref(LearningPath path, PathStep step, string stageId) =>
        $"{AgentLearningJourney.Href(stageId)}&path={Uri.EscapeDataString(path.Id)}&step={Uri.EscapeDataString(step.Id)}";

    /// <summary>The live-flow URL for a step's exercise, or null when the step has none.</summary>
    internal static string? ExerciseHref(LearningPath path, PathStep step) =>
        step.Exercise is { } exercise ? FlowDeepLink.Href(exercise.Setup with { Tour = $"{path.Id}:{step.Id}" }) : null;

    /// <summary>
    /// Resolves a <c>tour</c> value of the form <c>path-id:step-id</c> to a catalogue step that has an exercise.
    /// Anything else, including a step without an exercise, is null.
    /// </summary>
    internal static (LearningPath Path, PathStep Step)? ResolveTour(string? tour)
    {
        if (tour?.Split(':') is not [var pathId, var stepId])
        {
            return null;
        }

        return Find(pathId) is { } path && path.Find(stepId) is { Exercise: not null } step ? (path, step) : null;
    }

    /// <summary>The step URL for a lesson's return context, or null when the path or step is unknown.</summary>
    internal static string? ReturnHref(string? pathId, string? stepId) =>
        Find(pathId) is { } path && path.Find(stepId) is { } step ? StepHref(path, step) : null;
}
