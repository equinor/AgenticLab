namespace AgenticLab.Web.Learning;

internal sealed record LearningNode(string Id, string Title, string Detail);
internal sealed record LearningExample(string NodeId, string Text);

internal sealed record LearningStage(
    string Id,
    string Title,
    string Summary,
    string Takeaway,
    string RealityLabel,
    bool PlatformMap,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> HighlightedNodes,
    string? ActionLabel = null,
    string? ActionHref = null,
    bool Hidden = false,
    string? ParentId = null);

internal static class AgentLearningJourney
{
    internal static IReadOnlyList<LearningExample> HarnessExamples { get; } = Array.AsReadOnly<LearningExample>(
        AgentTurnStory.Responsibilities.Select(item => new LearningExample(item.NodeId, item.Output)).ToArray());

    internal static IReadOnlyList<LearningNode> IntroductionSteps { get; } = Array.AsReadOnly<LearningNode>(
    [
        // new("why", "Why", "Agents are becoming part of everyday work. Understanding them helps us see past the hype."),
        // new("definition", "What is an agent?", "An agent is a system that can observe its environment, make decisions, and take actions."),
        new("purpose", "Purpose", "An agent is a system that works toward a goal on your behalf, choosing its next steps and adjusting to results within the permissions and limits it has been given."),
        new("what", "What does this mean? / What makes this possible?", "An agent combines a model with software that manages context, tools and controls."),
        // new("how", "How does it work?", "Follow a task from request to model decision, tool use and result."),
    ]);

    internal static IReadOnlyList<LearningNode> Nodes { get; } = Array.AsReadOnly<LearningNode>(
    [
        new("harness", "Harness", "Supplies model input, checks tool requests and invokes permitted tools"),
        new("model", "Model", "Generates responses from supplied input, including text and, when enabled, tool requests"),
        new("context-management", "Context management", "Selects and assembles the information supplied to the model"),
        new("instructions", "Load instruction text", "Insert standing and task-specific guidance into the model request"),
        new("harness-context", "Assemble context", "Select messages and retrieved information for one model request"),
        new("available-tools", "Describe tools", "Supply tool names, descriptions and expected arguments; map accepted requests to implementations"),
        new("harness-memory", "Retain session messages", "Store selected messages and results, then choose what to include in a later request"),
        new("execution-controls", "Check tool requests", "Validate the requested tool and arguments against configured permissions, approvals and limits"),
        new("anatomy-persona", "Role instructions", "Instruction text describing this agent's task and approach, not its execution permissions"),
        new("anatomy-selected-tools", "Selected tools", "The configured subset exposed to this agent"),
        new("anatomy-settings", "Model choice", "The model selected for inference, not its execution permissions"),
        new("anatomy-task", "Task prompt", "The user's request for this turn"),
        new("anatomy-custom-instructions", "Custom instructions", "Applicable project guidance added to context"),
        new("anatomy-skills", "Skills", "Playbook descriptions, with a body loaded when needed"),
        new("loop-context", "Context management", "Assembles the task, instructions, selected session state and tool descriptions for each model request"),
        new("loop-decision", "Model response", "Generate text, tool requests or both from the supplied input"),
        new("loop-execute", "Execution", "Calls the model, handles responses and checks every tool request before invocation"),
        new("loop-observe", "Tool result", "Returned content or an error can be included in the next request"),
        new("loop-answer", "Response or stop", "Execution delivers the response or stops the turn; ending does not prove success"),
        new("connected-agent", "Agent", "Uses external capabilities when needed"),
        new("mcp-tools", "Tool server", "Exposes tools the agent can call"),
        new("a2a-agent", "Another agent", "Accepts delegated tasks and returns results"),
        new("hosting-runtime", "Harness", "Where context is managed and permitted actions execute"),
        new("hosting-model", "Model service", "Where inference happens, independently of the harness"),
        new("hosting-access", "Tools and data", "What the runtime can reach with its identity"),
        new("hosting-owner", "Operational owner", "Who handles availability, changes, failures and cost"),
        new("foundry-runtime", "Agent runtime", "Runs a prompt agent or your hosted agent code"),
        new("foundry-model", "Model deployment", "Provides the model used for inference"),
        new("foundry-tools", "Connected tools", "Built-in capabilities, custom functions or MCP servers"),
        new("lifecycle-run", "Run", "Invoke a version on real or representative tasks"),
        new("lifecycle-observe", "Observe", "Inspect traces, results and failures"),
        new("lifecycle-evaluate", "Evaluate", "Check quality and safety against repeatable tests"),
        new("lifecycle-improve", "Improve", "Adjust instructions, tools or controls; test the next version"),
    ]);

    private static IReadOnlyList<LearningStage> AllStages { get; } = Array.AsReadOnly<LearningStage>(
    [
        new("why-agents", "Intro",
            "AI agents, explained.",
            "Understand the parts. See the possibilities and limits.",
            "Introduction", false,
            ["agent", "guardrails"], []),
        new("model-to-agent", "Agent",
            "An agent combines a harness that handles input, manages context and controls execution with a model that generates responses.",
            "Agent = Harness + Model. The harness manages the work. The model generates responses from the supplied input.",
            "Available today", false,
            ["agent", "llm", "harness"], ["harness", "model"]),
        new("inside-the-harness", "Inside the harness",
            "Two responsibilities: context management and execution. Discover their parts one step at a time.",
            "Instruction text guides the model. Executable rules control which tool requests the harness allows.",
            "Available today", false,
            ["system-prompt", "context", "tools", "guardrails"],
            ["context-management", "instructions", "harness-context", "available-tools", "harness-memory", "execution-controls"],
            ParentId: "model-to-agent"),
        new("agent-loop", "The agent loop",
            "Assemble context, call the model and handle its response. Tool results feed into the next context; a response or a stop can end the turn.",
            "Check every tool request before execution. Return results to context management before calling the model again.",
            "Available today", false,
            ["reasoning", "tools", "context"], ["loop-context", "loop-decision", "loop-execute", "loop-answer"],
            "Open live flow", "/", ParentId: "model-to-agent"),
        new("anatomy-of-agent", "Anatomy of an agent",
            "Configuration selects instruction text, tools, model settings and execution rules. A task supplies the user's request for one turn.",
            "Text supplied to the model is different from rules executed by the harness. A role prompt or loaded skill does not grant permissions.",
            "Illustrative", false,
            ["persona", "tools", "custom-instructions", "skills"],
            ["instructions", "available-tools", "anatomy-persona", "anatomy-selected-tools", "anatomy-settings", "execution-controls", "anatomy-task", "anatomy-custom-instructions", "anatomy-skills"],
            ParentId: "model-to-agent"),
        new("agents-everywhere", "One foundation, many systems",
            "Purpose, hosting and triggers are separate choices. Compare illustrative configurations while the foundation stays the same.",
            "Tools and permissions change with the environment. A local harness does not mean a local model.",
            "Illustrative", false,
            ["where-agents-run", "environment", "tools", "guardrails"], ["harness", "model"]),
        new("agent-landscape", "Different purposes, same pattern",
            "Chat, coding, office and custom agents serve different purposes. Underneath, they share a recognizable foundation.",
            "Different purposes and products. The same underlying agentic principles.",
            "Illustrative", false,
            ["agent", "harness", "tools"], ["harness", "model"],
            ParentId: "agents-everywhere"),
        new("wider-ecosystem", "Connecting tools and agents",
            "Agents can call external tools or delegate work to other agents when needed. MCP and A2A standardize these optional connections.",
            "A protocol standardizes a connection. It does not, by itself, make that connection safe.",
            "Available today", false,
            ["mcp", "a2a", "environment"],
            ["connected-agent", "mcp-tools", "a2a-agent"],
            ParentId: "agents-everywhere"),
        new("where-to-run", "Where agents run",
            "Tools, data and connections inform where the harness should run. Compare operating models: a personal runtime, an existing product, your own service, or a managed agent platform.",
            "Production is a change in responsibilities, not just a change of address. Staying local can be the right choice.",
            "Illustrative", false,
            ["where-agents-run", "environment", "guardrails", "securing-agents"],
            ["hosting-runtime", "hosting-model", "hosting-access", "hosting-owner"]),
        new("map-to-foundry", "Map to Microsoft Foundry",
            "A Foundry project organizes agents and connected resources. The familiar concepts map to managed platform capabilities.",
            "Hosting an agent and providing its model are different responsibilities.",
            "Conceptual mapping", true,
            ["where-agents-run", "llm", "tools"],
            ["foundry-runtime", "foundry-model", "foundry-tools"], Hidden: true),
        new("run-and-improve", "Run and improve",
            "Use evidence from runs and repeatable tests to improve the next version. This cycle spans versions, not tool calls within a turn.",
            "A trace shows what happened. An evaluation checks how well it worked.",
            "Today + future integration", false,
            ["guardrails", "securing-agents"],
            ["lifecycle-run", "lifecycle-observe", "lifecycle-evaluate", "lifecycle-improve"],
            "Open live flow", "/", ParentId: "where-to-run"),
    ]);

    internal static IReadOnlyList<LearningStage> Stages { get; } =
        Array.AsReadOnly(AllStages.Where(stage => !stage.Hidden).ToArray());

    internal static IReadOnlyList<LearningStage> RootStages { get; } =
        Array.AsReadOnly(Stages.Where(stage => stage.ParentId is null).ToArray());

    internal static IEnumerable<LearningStage> Children(string id) =>
        Stages.Where(stage => stage.ParentId == id);

    internal static string Number(string? id)
    {
        var stage = Resolve(id);
        var rootId = stage.ParentId ?? stage.Id;
        var rootNumber = RootStages.TakeWhile(root => root.Id != rootId).Count() + 1;
        if (stage.ParentId is null)
        {
            return $"{rootNumber}";
        }

        var childNumber = Children(rootId).TakeWhile(child => child.Id != stage.Id).Count() + 1;
        return $"{rootNumber}.{childNumber}";
    }

    internal static LearningNode Node(string id) => Nodes.First(node => node.Id == id);

    internal static LearningStage Resolve(string? id) => Stages[IndexOf(id)];

    internal static int IndexOf(string? id)
    {
        for (var index = 0; index < Stages.Count; index++)
        {
            if (string.Equals(Stages[index].Id, id, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return 0;
    }

    internal static LearningStage Move(string? id, int offset) =>
        Stages[(int)Math.Clamp((long)IndexOf(id) + offset, 0, Stages.Count - 1)];

    internal static string Href(string id) => $"/learn?stage={Uri.EscapeDataString(id)}";
}