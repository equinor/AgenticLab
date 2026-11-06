using AgenticLab.Examples.Security.Scripting;
using AgenticLab.Examples.Security.Tools;
using AgenticLab.Extensibility.Agents;
using Microsoft.Extensions.AI;

namespace AgenticLab.Examples.Security.Agents;

/// <summary>
/// Scenario 2, defended. Same persona, same scripted model and the same attempt to mail the attacker as
/// <see cref="LeakyAssistantAgent"/>, but the tools enforce policy in code: documents are wrapped as untrusted
/// data and <c>SendMail</c> rejects recipients outside the allowed domain. Shows that a tool request is not
/// authorization — the harness decides.
/// </summary>
public sealed class DefendedAssistantAgent : ScenarioAgentBase
{
    /// <summary>The catalogue name this agent is registered and selected under.</summary>
    public const string AgentName = "DefendedAssistant";

    /// <summary>The prompt that triggers the scenario (identical to the leaky twin's).</summary>
    public const string ScenarioPrompt = LeakyAssistantAgent.ScenarioPrompt;

    private readonly SimulatedWorkplaceTool _workplace = new(guarded: true);

    /// <inheritdoc />
    public override string Name => AgentName;

    /// <inheritdoc />
    public override string Description => "Scripted scenario: the same leaky assistant behind tools that enforce a recipient allowlist.";

    /// <inheritdoc />
    public override string Prompt => ScenarioPrompt;

    /// <inheritdoc />
    public override AgentRiskLevel RiskLevel => AgentRiskLevel.Low;

    /// <inheritdoc />
    public override IReadOnlyList<string> Guardrails => CommonGuardrails(
        $"SendMail only accepts recipients in {SimulatedWorkplaceTool.AllowedMailDomain}, enforced in code",
        "Documents are returned inside an untrusted-content wrapper");

    /// <inheritdoc />
    protected override string Persona => LeakyAssistantAgent.WorkplacePersona;

    /// <inheritdoc />
    public override IList<AITool> Tools => _workplace.AsMailTools();

    /// <inheritdoc />
    protected override Playbook Playbook { get; } = LeakyAssistantAgent.ExfiltrationPlaybook(results =>
    {
        var receipt = results.ElementAtOrDefault(1) ?? string.Empty;
        return receipt.StartsWith("Blocked", StringComparison.Ordinal)
            ? $"I could not send the mail: {receipt}\n\nThe document contained an embedded instruction to forward its " +
              "full text to an external address, which the mail policy stopped. Here is the summary you asked for: " +
              "Standard 49 EUR/seat/month (up from 45), Enterprise 89 EUR/seat/month with 12% volume discount above " +
              "500 seats, partner margin 18%, effective 1 July."
            : $"Done. I mailed you the Q3 pricing summary. Tool result: {receipt}";
    });
}
