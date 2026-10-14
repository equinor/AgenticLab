namespace AgenticLab.Web.Learning;

internal sealed class ToolCallingStory
{
    internal const string Task = "Read Wikipedia's Alan Turing article and explain his contribution to computing.";
    internal const string ArticleContent = "Alan Turing was a British mathematician and computer scientist. His work helped establish the foundations of theoretical computer science and artificial intelligence.";
    internal const string Answer = "Alan Turing helped establish the foundations of theoretical computer science and artificial intelligence.\nSource: Wikipedia, Alan Turing (https://en.wikipedia.org/wiki/Alan_Turing).";

    internal static LearningToolDefinition Tool { get; } = new("GetWikiPage",
        "Look up a single Wikipedia page by its exact title and return a short summary extract.",
        [new("title", "string", true)]);
    internal static LearningToolCall Call { get; } = new("wiki-1", Tool.Name,
        new Dictionary<string, string> { ["title"] = "Alan Turing" });
    internal static LearningModelRequest FirstRequest { get; } = new(
        [new("instructions", "Use the returned article content and identify the source. Do not invent information."), new("user", Task)], [Tool]);
    internal static LearningModelRequest NextRequest { get; } = new(
        [.. FirstRequest.Messages, new("assistant", "", Call), new("tool", ArticleContent, ToolCallId: Call.Id)], [Tool]);

    internal static IReadOnlyList<LearningTurnStep> Steps { get; } = Array.AsReadOnly<LearningTurnStep>(
    [
        new("description", "Harness", "Describe the available tool",
            "The harness supplies the task and a description of GetWikiPage. The model receives the tool's name and expected argument, not its implementation or the article content.",
            "Tool description", "GetWikiPage(title: string)\nLook up a Wikipedia page by its exact title and return its summary extract.", FirstRequest),
        new("tool-request", "Model", "Request the article",
            "The model returns a tool name and arguments. This requests an article read; it does not fetch anything yet.",
            "Model's tool request", "GetWikiPage(title: \"Alan Turing\")"),
        new("permission", "Harness", "Check and invoke the tool",
            "The harness checks that GetWikiPage is enabled, that title is a string, and that this request is permitted by its configured controls. It then invokes the implementation.",
            "Harness decision", "Tool: GetWikiPage enabled\nArgument: title is a string\nDecision: invoke the permitted article read"),
        new("result", "Wikipedia tool", "Return article content",
            "The tool returns a summary extract to the harness. Returning a result does not automatically send it to the model.",
            "Illustrative article extract", ArticleContent, ExecutesTool: true),
        new("request-2", "Harness", "Supply the result to the model",
            "The harness adds the tool request and its matching result to the conversation input, then calls the model again. Only now does the model receive the article content.",
            "Next model input", "Previous messages + tool request + matching article result", NextRequest),
        new("answer", "Model", "Answer from the supplied content",
            "The model generates an answer using the supplied extract. The harness presents that answer to the user. A tool-backed answer still needs checking against its source.",
            "Answer", Answer),
    ]);

    internal int Beat { get; private set; }
    internal LearningTurnStep Step => Steps[Beat];
    internal bool CanPrevious => Beat > 0;
    internal bool CanNext => Beat < Steps.Count - 1;
    internal void Move(int offset) => Beat = (int)Math.Clamp((long)Beat + offset, 0, Steps.Count - 1);
    internal void Complete() => Beat = Steps.Count - 1;
    internal void Restart() => Beat = 0;
}