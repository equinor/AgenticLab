using AgenticLab.Examples.Security.Scripting;
using AgenticLab.Extensibility.Agents;
using Microsoft.Extensions.AI;

namespace AgenticLab.Examples.Security.Agents;

/// <summary>
/// Base for Security Lab agents: each one plays a fixed <see cref="Playbook"/> on an offline
/// <see cref="ScenarioModel"/> instead of a provider model. The persona is still sent in the (captured) request,
/// so the viewer sees a realistic prompt, but it does not influence the scripted behaviour; the point of the lab is
/// that a manipulated model ignores it.
/// </summary>
public abstract class ScenarioAgentBase : AgentDefinitionBase, IScriptedModelAgent
{
    /// <summary>The scripted turns this agent plays.</summary>
    protected abstract Playbook Playbook { get; }

    /// <summary>The prompt that triggers the scenario; also the agent's only suggested prompt.</summary>
    public abstract string Prompt { get; }

    /// <inheritdoc />
    public override string? ModelId => ScenarioModel.ModelId;

    /// <inheritdoc />
    public override IReadOnlyList<string> SuggestedPrompts => [Prompt];

    /// <inheritdoc />
    public IChatClient CreateModel() => new ScenarioModel(Playbook);

    /// <summary>The guardrails every scenario shares; subclasses append their own.</summary>
    protected static IReadOnlyList<string> CommonGuardrails(params string[] extra) =>
    [
        "Scripted, offline model: the same steps every run, no provider call",
        "Simulated tools over in-memory data: no network, mail, files or commands",
        "Individual tools can be toggled off per run; breakpoints pause before each tool",
        .. extra,
    ];
}
