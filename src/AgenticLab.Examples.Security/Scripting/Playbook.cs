namespace AgenticLab.Examples.Security.Scripting;

/// <summary>One scripted model turn: either a tool request or the final text answer.</summary>
public abstract record ScriptedStep;

/// <summary>
/// A tool request the scripted model makes with fixed arguments. The arguments are chosen to show what a
/// manipulated model would send; they are not derived from the tool results at run time.
/// </summary>
public sealed record ScriptedToolCall(string Tool, IReadOnlyDictionary<string, object?> Arguments) : ScriptedStep;

/// <summary>The final answer of a turn, composed from the tool results observed so far (in call order).</summary>
public sealed record ScriptedAnswer(Func<IReadOnlyList<string>, string> Compose) : ScriptedStep
{
    /// <summary>Creates an answer that ignores the tool results.</summary>
    public ScriptedAnswer(string text) : this(_ => text) { }
}

/// <summary>
/// The deterministic script a scenario model follows for the first user turn, step by step, plus the reply it
/// gives on any later turn. The last step must be a <see cref="ScriptedAnswer"/>.
/// </summary>
public sealed record Playbook(IReadOnlyList<ScriptedStep> Steps, string FollowUp)
{
    /// <summary>Validates that the script ends with an answer so the model always terminates a turn.</summary>
    public Playbook Validated() =>
        Steps.Count > 0 && Steps[^1] is ScriptedAnswer
            ? this
            : throw new InvalidOperationException("A playbook must end with a ScriptedAnswer.");
}
