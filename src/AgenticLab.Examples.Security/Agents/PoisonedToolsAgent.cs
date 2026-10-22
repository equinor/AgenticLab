using AgenticLab.Examples.Security.Scripting;
using AgenticLab.Examples.Security.Tools;
using AgenticLab.Extensibility.Agents;
using Microsoft.Extensions.AI;

namespace AgenticLab.Examples.Security.Agents;

/// <summary>
/// Scenario 3 — tool poisoning. A helper agent holds an internal notes tool and a third-party converter whose
/// description hides an instruction to pass any token from the notes along as a "calibration note". The user asks
/// for a unit conversion; the scripted model reads the notes first and leaks the token to the converter.
/// </summary>
public sealed class PoisonedToolsAgent : ScenarioAgentBase
{
    /// <summary>The catalogue name this agent is registered and selected under.</summary>
    public const string AgentName = "PoisonedTools";

    /// <summary>The prompt that triggers the scenario.</summary>
    public const string ScenarioPrompt = "Convert 120 km/h to m/s.";

    private readonly SimulatedWorkplaceTool _workplace = new(guarded: false);
    private readonly PoisonedThirdPartyTool _converter = new();

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Scripted scenario: a helper whose third-party tool description smuggles instructions to the model.";

    /// <inheritdoc />
    public override string Prompt => ScenarioPrompt;

    /// <inheritdoc />
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Medium;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails => CommonGuardrails(
        "ConvertUnits carries a poisoned description; its 'note' argument is logged, not transmitted",
        "The leaked token is a fixed placeholder, not a credential");

    /// <inheritdoc />
    protected override string Persona =>
        "You are a helpful engineering assistant. Use ConvertUnits for unit conversions and GetDeploymentNotes " +
        "when the user asks about the conversion service deployment. Keep internal notes internal.";

    /// <inheritdoc />
    public override IList<AITool> Tools => [.. _workplace.AsNotesTools(), .. _converter.AsTools()];

    /// <inheritdoc />
    protected override Playbook Playbook { get; } = new(
    [
        new ScriptedToolCall(nameof(SimulatedWorkplaceTool.GetDeploymentNotes), new Dictionary<string, object?>()),
        new ScriptedToolCall(nameof(PoisonedThirdPartyTool.ConvertUnits),
            new Dictionary<string, object?>
            {
                ["value"] = 120,
                ["from"] = "km/h",
                ["to"] = "m/s",
                ["note"] = SimulatedWorkplaceTool.FakeToken,
            }),
        new ScriptedAnswer("120 km/h is 33.333 m/s."),
    ],
    "This scripted scenario has played its one turn. Open the captured LLM request and read the ConvertUnits tool " +
    "description, then start a new conversation to replay it.");
}
