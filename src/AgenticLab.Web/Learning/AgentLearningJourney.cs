using AgenticLab.Web.Flow;

namespace AgenticLab.Web.Learning;

internal sealed record LearningNode(string Id, string Title, string Detail);
internal sealed record LearningExample(string NodeId, string Text);
internal sealed record LearningExchange(string From, string To, string Detail);
internal sealed record LearningDiagram(string Id, string Description, IReadOnlyList<LearningNode> Actors, IReadOnlyList<LearningExchange> Exchanges);

internal sealed record LearningStage(
    string Id,
    string Title,
    string Summary,
    string Takeaway,
    bool PlatformMap,
    IReadOnlyList<string> ConceptIds,
    IReadOnlyList<string> HighlightedNodes,
    string? ActionLabel = null,
    string? ActionHref = null,
    bool Hidden = false,
    string? ParentId = null);

internal static class AgentLearningJourney
{
    /// <summary>
    /// The "Show in live flow" link for the tool-calling lesson: the Wikipedia agent in the technical view with its
    /// tools switched off for the run and a general-knowledge question pre-filled, so one send shows the model
    /// answering directly instead of requesting a tool call.
    /// </summary>
    internal static string ToolCallingFlowLink { get; } = FlowDeepLink.Href(new FlowLinkRequest(
        Agent: "WikiAssistant",
        Prompt: "Who is the king of Norway",
        View: DiagramPreset.Technical,
        Tools: ["-SearchWiki", "-GetWikiPage"]));

    internal static IReadOnlyList<LearningExample> HarnessExamples { get; } = Array.AsReadOnly<LearningExample>(
        AgentTurnStory.Responsibilities.Select(item => new LearningExample(item.NodeId, item.Output)).ToArray());

    internal static IReadOnlyList<LearningNode> IntroductionSteps { get; } = Array.AsReadOnly<LearningNode>(
    [
        new("chatbot", "Chatbot application", "You input a message to the chatbot application and receive a reply in the same conversation."),
        new("agent", "Chatbot application", "Earlier versions of the ChatGPT application sends your message to a large language model (LLM) endpoint. The model generates a response, and the application shows it to you."),
        new("chat-interface", "Chat Agent", "Applications like ChatGPT can use tools like web search. The application manages the conversation, runs web searches when instructed by the LLM, and uses the LLM to generate a reply."),
        new("chat-to-agent", "Chat Agent", "What makes ChatGPT agentic is that the LLM reasons about what to do next, including when to request a web search from the application. The part of the application that manages context and executes tools is often referred to as the harness."),
    ]);

    private static IReadOnlyList<LearningExchange> IntroductionConversationExchanges { get; } = Array.AsReadOnly<LearningExchange>(
    [
        new("user", "app", "Message"),
        new("app", "user", "Reply"),
    ]);

    private static IReadOnlyList<LearningExchange> IntroductionExchanges { get; } = Array.AsReadOnly<LearningExchange>(
    [
        .. IntroductionConversationExchanges,
        new("app", "model", "Request"),
        new("model", "app", "Response"),
    ]);

    private static IReadOnlyList<LearningExchange> IntroductionSearchExchanges { get; } = Array.AsReadOnly<LearningExchange>(
    [
        .. IntroductionExchanges,
        new("app", "search", "Search"),
        new("search", "app", "Results"),
    ]);

    internal static IReadOnlyList<LearningDiagram> IntroductionDiagrams { get; } = Array.AsReadOnly<LearningDiagram>(
    [
        new("chatbot", "The user sends a message to the chat application and receives a reply.",
            [new("user", "User", "Sends a message"), new("app", "Chat application", "Shows the reply")],
            IntroductionConversationExchanges),
        new("agent", "Behind the conversation, the chat application sends the message to the LLM. The model generates a response, which the application returns to the user.",
            [new("user", "User", "Sends a message"), new("app", "Chat application", "Sends and receives"), new("model", "LLM", "Generates responses")],
            IntroductionExchanges),
        new("chat-interface", "ChatGPT can use web search to look up information before answering. The application connects the user, the LLM and web search. This is a simplified illustration, not a live integration or a specific search provider.",
            [new("user", "User", "Sends a message"), new("app", "Chat application", "ChatGPT"),
             new("model", "LLM", "Generates responses"), new("search", "Web search", "Finds online information")],
            IntroductionSearchExchanges),
        new("chat-to-agent", "In this simplified ChatGPT diagram, the chat application contains the harness, which includes web search. The user and LLM remain outside the application. Together, the harness and the model form the agent.",
            [new("user", "User", "Sends a message"), new("app", "Chat application", "ChatGPT"),
             new("model", "LLM", "Generates responses"), new("search", "Web search tool", "Finds online information")],
            IntroductionSearchExchanges),
    ]);

    internal static IReadOnlyList<LearningNode> Nodes { get; } = Array.AsReadOnly<LearningNode>(
    [
        new("harness", "Harness", "Supplies model input, checks tool requests and invokes permitted tools"),
        new("model", "Model", "Generates responses from supplied input, including text and, when enabled, tool requests"),
        new("wikipedia-tool", "Wikipedia tool", "Reads a Wikipedia article's summary extract when invoked by the harness"),
        new("context-management", "Context management", "Selects and assembles the information supplied to the model"),
        new("instructions", "Load instruction text", "Insert standing and task-specific guidance into the model request"),
        new("harness-context", "Assemble context", "Select messages and retrieved information for one model request"),
        new("context-history", "Conversation history", "Earlier messages selected for this request"),
        new("context-results", "Tool results", "Content returned by tools and selected for this request"),
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
            "What makes an application agentic?",
            "Together, the harness and the model form the agent.",
            false,
            [], []),
        new("model-to-agent", "Agent",
            "An agent consists of a harness and a model. Each has a different role.",
            "Agent = Harness + Model. The model generates responses and requests actions. The harness manages context and executes permitted actions.",
            false,
            ["agent", "llm", "harness"], ["harness", "model"]),
        new("inside-the-harness", "Context management",
            "The harness selects instructions, the task, conversation messages, tool results and tool descriptions to construct the context for the next model request.",
            "Context is the input selected for one model request.",
            false,
            ["system-prompt", "context", "tools"],
            ["context-management", "instructions", "harness-context", "context-history", "context-results", "available-tools"],
            ParentId: "model-to-agent"),
        new("tool-calling", "Model response",
            "The model reasons over the supplied context and chooses the next action to fulfill each request. It can answer directly with text or request a tool call from the harness.",
            "The model requests actions that the harness should execute.",
            false,
            ["tools", "llm", "harness", "guardrails"], ["model", "harness", "execution-controls"],
            "Show in live flow", ToolCallingFlowLink, ParentId: "model-to-agent"),
        new("agent-loop", "The agent loop",
            "The harness constructs context and calls the model. The model returns an answer or requests a tool call. The harness handles the response and continues the loop or ends the turn.",
            "Tool results return to context management for the next model request. The loop ends when the harness returns an answer or stops the turn.",
            false,
            ["reasoning", "tools", "context"], ["loop-context", "loop-decision", "loop-execute", "loop-answer"],
            "Open live flow", "/flow", ParentId: "model-to-agent"),
        new("anatomy-of-agent", "Anatomy of an agent",
            "Configuration selects instruction text, tools, model settings and execution rules. A task supplies the user's request for one turn.",
            "Text supplied to the model is different from rules executed by the harness. A role prompt or loaded skill does not grant permissions.",
            false,
            ["persona", "tools", "custom-instructions", "skills"],
            ["instructions", "available-tools", "anatomy-persona", "anatomy-selected-tools", "anatomy-settings", "execution-controls", "anatomy-task", "anatomy-custom-instructions", "anatomy-skills"],
            ParentId: "model-to-agent"),
        new("agents-everywhere", "One foundation, many systems",
            "Purpose, hosting and triggers are separate choices. Compare illustrative configurations while the foundation stays the same.",
            "Tools and permissions change with the environment. A local harness does not mean a local model.",
            false,
            ["where-agents-run", "environment", "tools", "guardrails"], ["harness", "model"]),
        new("agent-landscape", "Different purposes, same pattern",
            "Chat, coding, office and custom agents serve different purposes. Underneath, they share a recognizable foundation.",
            "Different purposes and products. The same underlying agentic principles.",
            false,
            ["agent", "harness", "tools"], ["harness", "model"],
            ParentId: "agents-everywhere"),
        new("wider-ecosystem", "Connecting tools and agents",
            "Agents can call external tools or delegate work to other agents when needed. MCP and A2A standardize these optional connections.",
            "A protocol standardizes a connection. It does not, by itself, make that connection safe.",
            false,
            ["mcp", "a2a", "environment"],
            ["connected-agent", "mcp-tools", "a2a-agent"],
            ParentId: "agents-everywhere"),
        new("where-to-run", "Where agents run",
            "Tools, data and connections inform where the harness should run. Compare operating models: a personal runtime, an existing product, your own service, or a managed agent platform.",
            "Production is a change in responsibilities, not just a change of address. Staying local can be the right choice.",
            false,
            ["where-agents-run", "environment", "guardrails", "securing-agents"],
            ["hosting-runtime", "hosting-model", "hosting-access", "hosting-owner"]),
        new("map-to-foundry", "Map to Microsoft Foundry",
            "A Foundry project organizes agents and connected resources. The familiar concepts map to managed platform capabilities.",
            "Hosting an agent and providing its model are different responsibilities.",
            true,
            ["where-agents-run", "llm", "tools"],
            ["foundry-runtime", "foundry-model", "foundry-tools"], Hidden: true),
        new("run-and-improve", "Run and improve",
            "Use evidence from runs and repeatable tests to improve the next version. This cycle spans versions, not tool calls within a turn.",
            "A trace shows what happened. An evaluation checks how well it worked.",
            false,
            ["guardrails", "securing-agents"],
            ["lifecycle-run", "lifecycle-observe", "lifecycle-evaluate", "lifecycle-improve"],
            "Open live flow", "/flow", ParentId: "where-to-run"),
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