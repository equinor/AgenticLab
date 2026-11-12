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
            [
                new("tool-selection", "Offer one tool at a time",
                    "Tool selection changes what the harness offers the model on the next run. This exercise offers Wikipedia search but not page retrieval. A search result and a full page are different evidence.",
                    "Can the model retrieve a full page when only SearchWiki is offered?",
                    "Open the exercise and send the prepared question. Inspect the offered tools and any returned search results.",
                    ["Did the model use the available tool? Which claims could you check against its result?"],
                    [],
                    new(new FlowLinkRequest(Host: "default", Agent: WikiAgent,
                            Prompt: "Search Wikipedia for Norway and summarize the search results.",
                            View: DiagramPreset.Technical, Tools: ["SearchWiki", "-GetWikiPage"]),
                        "Press Send. Inspect the model request and any tool calls in Execution; GetWikiPage should not be offered.",
                        WikiTools,
                        ["SearchWiki is offered; GetWikiPage is not.", "A model may answer without calling a tool. Do not infer tool use from the answer alone."],
                        [new("Harness", "Offers only SearchWiki."), new("Model", "May request a search or answer directly."),
                         new("Harness", "Runs a permitted search if requested, then passes the result back to the model.") ])),
                new("pause-and-step", "Pause before an action",
                    "A breakpoint pauses the harness at an execution boundary. Manual mode advances between boundaries; it does not step through the model's internal reasoning. Stopping prevents further work but does not undo actions already taken.",
                    "At a before-tool pause, has the tool already run?",
                    "Open the exercise, send the question and advance through the pauses. Inspect a tool request before allowing it to proceed, or stop the run.",
                    ["Which event distinguishes a requested action from an executed action? What would Stop leave unchanged?"],
                    [],
                    new(new FlowLinkRequest(Host: "default", Agent: WikiAgent,
                            Prompt: "Use Wikipedia to find Norway's capital and cite the page.", View: DiagramPreset.Technical,
                            Mode: FlowMode.Manual, Breakpoints: ["before-tool"], Tools: WikiTools),
                        "Press Send, then use the execution controls to advance. At a before-tool pause, inspect the arguments before continuing or stopping.",
                        WikiTools,
                        ["The paused boundary and tool arguments, before a tool result exists.", "If no tool is requested, there is no before-tool pause. Follow the model boundaries instead."],
                        [new("Model", "Requests a Wikipedia search."), new("Harness", "Pauses before invoking the tool."),
                         new("User", "Inspects the request, then advances or stops."), new("Harness", "Invokes the tool only if execution continues.") ])),
                new("replay", "Read a captured run",
                    "Execution replay moves through captured events. It does not rerun tools or call the model. The selected point bounds the evidence shown; later results are not evidence available to an earlier decision.",
                    "Does moving backward in replay undo a tool's effect?",
                    "Open the exercise and let the run finish. Move backward and forward through Execution and compare the request, tool result and answer.",
                    ["Where did new information enter the context? Which events were absent if the model answered directly?"],
                    [],
                    new(new FlowLinkRequest(Host: "default", Agent: WikiAgent,
                            Prompt: "Use Wikipedia to find Norway's capital and cite the page.", View: DiagramPreset.Technical,
                            Mode: FlowMode.Auto, Tools: WikiTools),
                        "Press Send and let the run finish. In Execution, select earlier events and return to the final event without sending another message.",
                        WikiTools,
                        ["The captured sequence, not a new execution.", "A tool result enters a later model request only if a tool was called."],
                        [new("Harness", "Captures model and tool events during the run."), new("User", "Selects an earlier event after completion."),
                         new("Application", "Shows the captured evidence at that point without executing it again.") ])),
                new("compare-models", "Compare models fairly",
                    "Models can differ in answers, tool choices, latency and token use. A fair comparison keeps the prompt, agent and offered tools fixed and starts with fresh conversations. One run is an observation, not a benchmark.",
                    "Could a faster answer still require more model calls?",
                    "Read Run and improve. In live flow, choose an available model, send a short question and record the answer's usage figures. Start a fresh conversation, choose another listed model and repeat with the same agent, question and tools. If only one model is listed, outline the comparison without running it. Live runs can incur charges.",
                    ["Compare correctness, duration, model time, tokens and tool calls. Which differences need repeated trials?"],
                    ["run-and-improve"]),
                new("discovery", "Distinguish tools from delegates",
                    "MCP publishes tools for a harness to call. A2A exposes remote agents to delegate tasks to. Discovery lists capabilities; it neither proves they are safe nor shows that a particular run used them.",
                    "Is a discovered remote agent automatically available to every local agent?",
                    "Read Connecting tools and agents. In live flow, open Discovery and inspect the selected agent's groups, then the other groups. Compare an MCP tool's input with an A2A agent's purpose. Do not run rediscovery just to inspect the current list. If no protocol service is available, use the lesson's examples.",
                    ["Which capability would execute a specific operation, and which would receive a delegated task? What evidence would show it was actually called?"],
                    ["wider-ecosystem"]),
                new("workspace-context", "Choose workspace context",
                    "Skills supply task-specific instructions when loaded; custom instructions add guidance to context. Neither grants permissions. Workspace file and terminal tools act on the server's configured workspace, not arbitrary files in your browser.",
                    "Does listing a skill mean its full instructions were sent to the model?",
                    "Read Context management. On a trusted local instance, select a workspace-capable agent and the repository's sample-workspace folder. Inspect its discovered skills and instructions in Settings. Compare listing a skill with loading it. Do not use private files. Hosted read-only or disabled workspaces may not support this exercise; in that case, identify which context each item would add on paper.",
                    ["Which context is always included, which is selected, and which requires a tool call? Where are permissions enforced?"],
                    ["inside-the-harness"]),
                new("reflect", "Reflect",
                    "Run configuration controls what is offered and when execution can proceed. Captures show what happened, while replay only changes which part you inspect.",
                    null, null,
                    ["How do a tool toggle, a breakpoint and replay differ?", "What would you hold constant when comparing two models?", "Why are discovered capabilities and loaded context not authorization?"], []),
            ], []),
        new("advanced", PathLevel.Advanced, "Investigate and extend",
            "Inspect what was sent to the model, see how agents are attacked, and add your own.",
            [
                new("captured-context", "Trace a claim to its input",
                    "Captured requests show the context supplied to the model. The prompt signature summarizes its structure, not the model's private reasoning. A plausible answer is not proof that the harness supplied supporting evidence.",
                    "Where would you find evidence that a tool result was included in a later request?",
                    "Open the exercise, finish a run and inspect its captured model requests. Trace one answer claim back to the available input, or note that no supporting input is visible.",
                    ["Which parts came from the host, persona, user and tools? What cannot be established from the capture?"],
                    [],
                    new(new FlowLinkRequest(Host: "default", Agent: WikiAgent,
                            Prompt: "Use Wikipedia to find Norway's capital and cite the page.",
                            View: DiagramPreset.Technical, ExpandHarness: true, Mode: FlowMode.Auto, Tools: WikiTools),
                        "Press Send. Inspect captured model requests and the prompt signature; compare the first request with a later request if a tool was used.",
                        WikiTools,
                        ["Instructions, task and offered tools in the first request.", "A tool result in a subsequent request if one exists; captures do not expose private reasoning."],
                        [new("Harness", "Builds and captures the first request."), new("Model", "May request a tool."),
                         new("Harness", "Adds the returned result to a later request."), new("User", "Checks an answer claim against the captured inputs.") ])),
                new("security-scenario", "Inspect an unsafe tool request",
                    "The Security Lab replays scripted scenarios with in-process fakes. In this scenario a document contains an instruction that redirects a mail tool. No real mail, network request or model-provider call occurs in the scenario.",
                    "Can an ordinary document become an instruction merely because the model treats it as one?",
                    "Open the exercise when the optional Security Lab is enabled. Inspect each before-tool pause and compare the user's request with the mail recipients. Without the module, follow the written walkthrough.",
                    ["At which point did document content cross a trust boundary? Would a harmless-looking final answer reveal the problem?"],
                    [],
                    new(new FlowLinkRequest(Host: "security", Agent: "LeakyAssistant",
                            Prompt: "Summarize the shared document and email it to me.", View: DiagramPreset.Technical,
                            ExpandHarness: true, Mode: FlowMode.Auto, Breakpoints: ["before-tool"],
                            Tools: ["ReadSharedDocument", "SendMail"]),
                        "Press Send. At each before-tool pause inspect the arguments, then advance. Compare SendMail's recipients with the user's request. All tools here are simulated.",
                        ["ReadSharedDocument", "SendMail"],
                        ["The document's injected note, then a mail request that includes an external recipient.", "A simulated receipt, not actual delivery. Stop prevents later calls but does not undo earlier ones."],
                        [new("User", "Requests a summary for themselves."), new("Tool", "Returns a document with an injected instruction."),
                         new("Scripted model", "Requests mail to the user and an external address."), new("Harness", "Pauses before the simulated mail tool executes.") ])),
                new("defended-boundary", "Compare a code-enforced boundary",
                    "The defended twin uses the same document but labels it untrusted and rejects external mail recipients in tool code. The rejection, not the warning text alone, enforces the boundary. Scripted outcomes demonstrate a mechanism, not a security evaluation of a real model.",
                    "What happens if the model still requests an external recipient despite the warning?",
                    "Open the defended exercise and compare its tool result with the previous scenario. Without Security Lab, compare the two written walkthroughs.",
                    ["Which protection depends on model compliance, and which runs regardless of it? What further tests would a real mail integration need?"],
                    [],
                    new(new FlowLinkRequest(Host: "security", Agent: "DefendedAssistant",
                            Prompt: "Summarize the shared document and email it to me.", View: DiagramPreset.Technical,
                            ExpandHarness: true, Mode: FlowMode.Auto, Breakpoints: ["before-tool"],
                            Tools: ["ReadSharedDocument", "SendMail"]),
                        "Press Send. Inspect the mail arguments at the pause, advance and read the rejection returned by the simulated tool. Compare it with the previous scenario.",
                        ["ReadSharedDocument", "SendMail"],
                        ["Untrusted document content remains visible in context.", "The mail tool rejects a recipient outside contoso.example in code."],
                        [new("Tool", "Returns the same document, marked as untrusted."), new("Scripted model", "Still requests an external recipient."),
                         new("Tool", "Rejects the request through its recipient check."), new("Harness", "Returns the rejection to the scripted model.") ])),
                new("workspace-agent", "Define a workspace agent",
                    "A workspace agent file supplies a persona and a selection of existing backend tools. It does not install new executable capabilities. Workspace access remains subject to the server's policy and is intended for trusted local use.",
                    "Would naming a nonexistent tool in an agent file make that tool available?",
                    "Read Context management. In a local checkout, inspect sample-workspace/agents/guide.agent.yaml and docs/workspace.md. Copy the sample to a new .agent.yaml file under your own trusted workspace's agents folder, give it a unique name and a narrow persona, and retain only tools documented as supported. Load that workspace in live flow and inspect the discovered agent before sending anything. On a hosted read-only instance, draft the file locally instead.",
                    ["What changed in the persona and tool list? Which permissions cannot be granted by this file?"],
                    ["inside-the-harness"]),
                new("example-module", "Plan an example module",
                    "An example module owns its domain agents, tools, assets and tests. Shared contracts connect it to the hosts. New domain state belongs in the module, not in the core Flow state or executable service hosts.",
                    "Which parts of an example can be shared without moving domain behavior into a host?",
                    "Read Different purposes, same pattern. In a local checkout, follow docs/examples.md and inspect src/AgenticLab.Examples.ChatGpt as a small example. Sketch a module with one agent and a harmless tool, list its host-role registrations and write a fake-data test plan. Identify the files you would add before implementing it; this step does not generate or register a module.",
                    ["Which contracts does the module need? How would you test its tools without credentials or calls to a model provider?"],
                    ["agent-landscape"]),
                new("hosting", "Choose a runtime and checks",
                    "The harness's location, the model provider and the trigger are separate choices. Moving to a service changes identity, data access and operational responsibilities; it does not make an agent reliable or safe by itself.",
                    "Can a local harness still send private context to a remote model?",
                    "Read Where agents run, Run and improve, and Security/AppSec. For the module you sketched, choose a runtime and trigger. List the data leaving each boundary, required identities, tool permissions, timeouts, cancellation behavior and evaluation cases. Do not deploy anything for this step.",
                    ["Which failure must stop execution? What evidence and repeated tests would you require before deployment?"],
                    ["where-to-run", "run-and-improve", "what-can-go-wrong"]),
                new("reflect", "Reflect",
                    "An investigation separates captured evidence from inference. An extension needs explicit capability boundaries, tests and an operational owner, not only a persona prompt.",
                    null, null,
                    ["What does a capture prove, and what remains unknown?", "Which boundary in the defended scenario was enforced by code?", "When would you use a workspace agent rather than an example module?"], []),
            ], []),
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
