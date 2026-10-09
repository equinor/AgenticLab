using System.Text.Json;

namespace AgenticLab.Web.Learning;

internal sealed record LearningToolArgument(string Name, string Type, bool Required);
internal sealed record LearningToolDefinition(string Name, string Description, IReadOnlyList<LearningToolArgument> Arguments);
internal sealed record LearningToolCall(string Id, string Name, IReadOnlyDictionary<string, string> Arguments);
internal sealed record LearningTurnMessage(string Role, string Text, LearningToolCall? ToolCall = null, string? ToolCallId = null);
internal sealed record LearningModelRequest(IReadOnlyList<LearningTurnMessage> Messages, IReadOnlyList<LearningToolDefinition> Tools);
internal sealed record LearningTurnStep(
    string Id, string Actor, string Title, string Explanation, string EvidenceLabel,
    string Evidence, LearningModelRequest? Request = null, bool ExecutesTool = false, string? Outcome = null)
{
    internal string LoopNodeId => Id switch
    {
        "task" => "context",
        "request-1" or "tool-request" or "request-2" => "model",
        "permission" or "stop" => "execute",
        "result" => "observation",
        "answer" or "finish" => "answer",
        _ => throw new InvalidOperationException($"No loop node for step '{Id}'."),
    };
}
internal sealed record LearningTurnScenario(string Id, string Title, IReadOnlyList<LearningTurnStep> Steps);
internal sealed record HarnessResponsibility(string NodeId, string Input, string Operation, string Output, string Boundary);

internal sealed class AgentTurnStory
{
    internal const string Task = "Read meeting-notes.txt and list the agreed actions.";
    internal const string Instructions = "List only actions supported by the notes. Include each owner and deadline when stated. Mark missing information as not stated. Do not edit files.";
    internal const string FileContent = "Meeting notes\n- Priya will send the revised budget by Friday.\n- Sam will book a review meeting; no deadline was agreed.\n- Update the project checklist; no owner or deadline was agreed.";
    internal const string Answer = "1. Send the revised budget. Owner: Priya. Deadline: Friday.\n2. Book a review meeting. Owner: Sam. Deadline: not stated.\n3. Update the project checklist. Owner: not stated. Deadline: not stated.";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    internal static LearningToolDefinition ReadTool { get; } = new(
        "read_file", "Return the text of a permitted file.", [new("path", "string", true)]);
    internal static LearningToolCall ReadCall { get; } = new(
        "read-1", ReadTool.Name, new Dictionary<string, string> { ["path"] = "meeting-notes.txt" });
    internal static LearningModelRequest FirstRequest { get; } = new(
        [new("instructions", Instructions), new("user", Task)], [ReadTool]);

    internal static IReadOnlyList<HarnessResponsibility> Responsibilities { get; } = Array.AsReadOnly<HarnessResponsibility>(
    [
        new("harness-context", "The user's request and any selected earlier messages.",
            "Select the messages for the next model request. Add retrieved content only after it is available.",
            "The first request includes the task but not the file's contents. The second also includes the read result.",
            "Context is the input supplied for one request. The model does not automatically see the workspace or all stored history."),
        new("instructions", "Standing guidance and any applicable project instructions.",
            "Insert the selected instruction text into the model request.", Instructions,
            "Instruction text can ask the model not to edit files. Only executable controls can prevent a write."),
        new("available-tools", "An enabled read_file implementation and its argument definition.",
            "Supply the tool name, description and expected arguments to the model. Map accepted requests to the implementation.",
            "read_file(path: string). The model supplies a file name; the implementation reads the file.",
            "A tool description is not its code, and advertising a tool does not authorize every invocation."),
        new("harness-memory", "Messages, tool requests and results from the session.",
            "Store selected messages and results. Choose which stored items to include in a later request.",
            "The retained read request and file result can be included when the user asks a follow-up question.",
            "Retaining a conversation does not retrain the model or give it unlimited context. Storage and selection depend on the harness."),
        new("execution-controls", "The requested tool name and path, plus the configured execution policy.",
            "Check that the tool is enabled and the path is allowed. Apply approval rules and limits before invoking it.",
            "In this example, read_file may read meeting-notes.txt. No file-writing or terminal tool is enabled.",
            "An allowed request can still fail because the file is missing or the operating system denies access. These example checks are not a filesystem sandbox."),
    ]);

    internal static IReadOnlyList<LearningTurnScenario> Scenarios { get; } = Array.AsReadOnly<LearningTurnScenario>(
    [
        ReadScenario("read", "Read succeeds", FileContent, Answer, true),
        ReadScenario("denied", "Read denied", "Read denied: this file is outside the configured allowed paths.",
            "I could not read the notes because access was denied. Provide an allowed copy or paste the notes; I cannot list actions from content I have not received.", false),
        ReadScenario("missing", "File missing", "File not found: meeting-notes.txt.",
            "The file was not found. Check the file name or provide the notes; I cannot list the agreed actions yet.", true),
        StoppedScenario("cancelled", "User cancels", "The user cancels before the file read starts.",
            "Cancelled. No tool was invoked and no action list was produced."),
        StoppedScenario("limit", "Limit reached", "The configured tool-call limit is zero. The harness refuses the requested read and stops this run.",
            "Stopped by a configured limit. No tool was invoked and no action list was produced."),
        DirectScenario(),
    ]);

    internal LearningTurnScenario Scenario { get; private set; } = Scenarios[0];
    internal int Beat { get; private set; }
    internal bool ShowAll { get; private set; }
    internal LearningTurnStep Step => Scenario.Steps[Beat];
    internal bool CanPrevious => Beat > 0;
    internal bool CanNext => Beat < Scenario.Steps.Count - 1;

    internal void Move(int offset)
    {
        Beat = (int)Math.Clamp((long)Beat + offset, 0, Scenario.Steps.Count - 1);
        ShowAll = false;
    }

    internal void Select(string id)
    {
        var scenario = Scenarios.FirstOrDefault(item => item.Id == id);
        if (scenario is null || scenario == Scenario) return;
        Scenario = scenario;
        Restart();
    }

    internal void Complete()
    {
        Beat = Scenario.Steps.Count - 1;
        ShowAll = true;
    }

    internal void Restart()
    {
        Beat = 0;
        ShowAll = false;
    }

    internal static string Json<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static LearningTurnStep Received(string task) => new(
        "task", "User", "Submit the task", "A turn starts with this request. A session can contain several turns and retain their messages.",
        "User message", task);

    private static LearningTurnStep RequestStep(string id, string title, LearningModelRequest request, string explanation) => new(
        id, "Harness", title, explanation, "Model request", Json(request), request);

    private static LearningTurnStep CallStep() => new(
        "tool-request", "Model", "Request a file read",
        "The model returns a tool name and arguments. This is a request to run code, not a file read. A response can also include text alongside tool requests.",
        "Tool request", Json(ReadCall));

    private static LearningTurnScenario ReadScenario(string id, string title, string result, string answer, bool executes)
    {
        var succeeded = id == "read";
        var nextRequest = new LearningModelRequest(
            [.. FirstRequest.Messages, new("assistant", "", ReadCall), new("tool", result, ToolCallId: ReadCall.Id)], [ReadTool]);
        return new(id, title,
        [
            Received(Task),
            RequestStep("request-1", "Send the first model request", FirstRequest,
                "The harness supplies instruction text, the user message and a tool definition. The file name is present; its contents are not. Context means the input supplied for this request."),
            CallStep(),
            new("permission", "Harness", executes ? "Allow the requested read" : "Deny the requested read",
                executes ? "The harness checks the enabled tool, the required path argument and the configured allowed path. This example permits this read without an approval prompt."
                    : "The model can request a file even when the execution policy forbids it. The harness refuses the invocation before any file-reading code runs.",
                "Configured decision", executes ? "Tool: read_file enabled\nArgument: path is a string\nAllowed path: meeting-notes.txt\nDecision: allow\nFile writes and terminal commands: no enabled tools"
                    : "Tool: read_file enabled\nRequested path: meeting-notes.txt\nAllowed paths: none\nDecision: deny"),
            new("result", executes ? "File-reading tool" : "Harness", executes ? "Return the read result" : "Return the denial",
                succeeded ? "The harness invokes the tool. The tool reads the permitted file and returns its text. The model has not received that text yet."
                    : executes ? "Permission to attempt a read does not guarantee success. The tool returns a missing-file error, not invented notes."
                    : "The harness produces an error result for the denied request. No file was opened.",
                succeeded ? "File content returned" : "Error returned", result, ExecutesTool: executes),
            RequestStep("request-2", "Send the result in a second request", nextRequest,
                "The harness includes the previous messages, the tool request and its matching result. The result identifier connects it to read-1. The model now receives the returned content or error."),
            new("answer", "Model", succeeded ? "Generate the action list" : "Report the missing evidence",
                succeeded ? "The model generates text from the supplied notes. Unstated owners and deadlines remain not stated. A generated answer still needs checking against its sources."
                    : "The model receives an error instead of notes. This example reports the limitation rather than claiming to have completed the task.",
                "Model response", answer),
            new("finish", "Harness", "End this turn",
                "In this example, the harness presents the response and retains the messages and results for the session. Later requests receive only the stored information the harness selects; retention does not retrain the model.",
                "Run outcome", succeeded ? "Action list delivered. No file was changed." : "Response delivered, but the requested action list was not produced.",
                Outcome: succeeded ? "answered" : "incomplete"),
        ]);
    }

    private static LearningTurnScenario StoppedScenario(string id, string title, string reason, string outcome) => new(id, title,
    [
        Received(Task),
        RequestStep("request-1", "Send the first model request", FirstRequest, "The harness supplies the task and read tool definition, without file content."),
        CallStep(),
        new("stop", "Harness", "Stop before execution", reason, "Run outcome", outcome, Outcome: id),
    ]);

    private static LearningTurnScenario DirectScenario()
    {
        var task = $"List the agreed actions from these notes:\n{FileContent}";
        var request = new LearningModelRequest([new("instructions", Instructions), new("user", task)], []);
        return new("provided", "Notes already supplied",
        [
            Received(task),
            RequestStep("request-1", "Send the supplied notes", request, "The user included the notes in the message. This request needs no file-reading tool."),
            new("answer", "Model", "Generate an answer without a tool call", "The model can answer from the content already supplied. Not every turn needs a tool.", "Model response", Answer),
            new("finish", "Harness", "End this turn", "The harness presents the response. One model request was sufficient for this example.", "Run outcome", "Action list delivered. No tool was invoked.", Outcome: "answered"),
        ]);
    }
}