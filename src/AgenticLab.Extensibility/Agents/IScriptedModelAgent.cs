using Microsoft.Extensions.AI;

namespace AgenticLab.Extensibility.Agents;

/// <summary>
/// An agent definition that supplies its own raw model instead of the host's configured provider, for
/// deterministic demonstrations. The host still wraps the client in its tool-filtering, function-invocation
/// and capture pipeline, so per-run tool toggles, breakpoints and captured payloads behave as for any agent.
/// </summary>
public interface IScriptedModelAgent : IAgentDefinition
{
    /// <summary>Creates the unwrapped model client; called once when the host builds its catalogue.</summary>
    IChatClient CreateModel();
}
