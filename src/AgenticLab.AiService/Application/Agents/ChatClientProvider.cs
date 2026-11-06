using System.Collections.Concurrent;
using AgenticLab.ModelProviders;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Agents;

/// <summary>
/// Caches one <see cref="IChatClient"/> per agent default model within the selected startup provider.
/// All clients share connection settings and the same tool filtering, invocation, capture and telemetry
/// pipeline, and route each call to the run's <see cref="ModelSelectionScope.Model"/> when one is selected.
/// Harness branding never changes the inference provider.
/// </summary>
public sealed class ChatClientProvider
{
    private readonly IConfiguration _configuration;
    private readonly ModelConnectionOptions _connection;
    private readonly ModelClientFactory _factory;
    private readonly ConcurrentDictionary<string, IChatClient> _clients;
    private readonly ConcurrentDictionary<string, IChatClient> _models;

    /// <summary>
    /// Reads the selected provider's server-side connection and model settings, defaulting to Azure OpenAI.
    /// </summary>
    /// <param name="configuration">Provider settings, the <c>Models:Choices</c> allow-list and optional <c>Agents:{Name}:Model</c> overrides; legacy deployment overrides apply only to Azure.</param>
    /// <exception cref="InvalidOperationException">Thrown when the selected provider's configuration or model allow-list is missing or invalid.</exception>
    public ChatClientProvider(IConfiguration configuration)
    {
        _configuration = configuration;
        _connection = new ModelConnectionOptions(configuration);
        Choices = new ModelChoices(configuration, _connection);
        _factory = new ModelClientFactory(_connection);
        var comparer = _connection.Provider == ModelProvider.AzureOpenAI
            ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        _clients = new(comparer);
        _models = new(comparer);
    }

    /// <summary>The default model identifier, or Azure deployment name, retained under its original API name.</summary>
    public string DefaultDeployment => _connection.DefaultModel;

    /// <summary>The models a caller may select per run; the only values <see cref="ModelSelectionScope"/> may carry.</summary>
    public ModelChoices Choices { get; }

    /// <summary>
    /// Returns the pipeline-wrapped chat client for the given deployment, creating and caching it on first
    /// use. A null or blank deployment resolves to the <see cref="DefaultDeployment"/>.
    /// </summary>
    /// <param name="deployment">The model identifier or Azure deployment name, or null/blank for the default.</param>
    /// <returns>The shared <see cref="IChatClient"/> for that deployment.</returns>
    public IChatClient Get(string? deployment)
    {
        var name = string.IsNullOrWhiteSpace(deployment) ? DefaultDeployment : deployment.Trim();
        return _clients.GetOrAdd(name, Build);
    }

    /// <summary>
    /// Resolves the declared model from <c>Agents:{Name}:Model</c>, then Azure-only
    /// <c>Agents:{Name}:Deployment</c>, then the agent's <see cref="IAgentDefinition.ModelId"/>,
    /// then the selected provider's <see cref="DefaultDeployment"/>.
    /// </summary>
    /// <param name="definition">The agent whose deployment to resolve.</param>
    /// <returns>The effective deployment name for the agent.</returns>
    public string ResolveDeployment(IAgentDefinition definition)
    {
        var configured = _configuration[$"Agents:{definition.Name}:Model"];
        if (string.IsNullOrWhiteSpace(configured) && _connection.Provider == ModelProvider.AzureOpenAI)
            configured = _configuration[$"Agents:{definition.Name}:Deployment"];
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured.Trim();
        }

        return string.IsNullOrWhiteSpace(definition.ModelId) ? DefaultDeployment : definition.ModelId!.Trim();
    }

    /// <summary>
    /// Resolves the deployment an agent actually <em>executes</em> on, which is normally the same as its
    /// declared model from <see cref="ResolveDeployment"/>. <c>Models:ForceDefaultModel</c> overrides
    /// execution without changing the display label. When absent, Azure retains its legacy
    /// <c>AzureOpenAI:ForceDefaultModel</c> setting; native providers do not inherit that flag. A model
    /// selected for a run (<see cref="ModelSelectionScope.Model"/>) replaces this at call time.
    /// </summary>
    /// <param name="declaredDeployment">The agent's declared (display) deployment from <see cref="ResolveDeployment"/>.</param>
    /// <returns>The deployment the agent's chat client is built on.</returns>
    public string ExecutionDeployment(string declaredDeployment) =>
        _connection.ForceDefaultModel || string.IsNullOrWhiteSpace(declaredDeployment)
            ? DefaultDeployment
            : declaredDeployment.Trim();

    // The selection is resolved below function invocation, so every round-trip of a run uses the same model.
    private IChatClient Build(string deployment) =>
        Pipeline(new ModelRoutingChatClient(deployment, () => ModelSelectionScope.Current?.Model, Model));

    private IChatClient Model(string model) => _models.GetOrAdd(model, id => Instrument(_factory.Create(id), id));

    /// <summary>
    /// Wraps a raw model client in the shared tool filtering, flow-controlled function invocation, capture,
    /// usage and telemetry pipeline. Used for <see cref="IScriptedModelAgent"/> models, so a scripted model
    /// is subject to the same per-run controls as a real one. It never routes to a selected model.
    /// </summary>
    /// <param name="raw">The unwrapped model client.</param>
    /// <param name="modelLabel">The model name recorded in usage and telemetry; defaults to <c>scripted</c>.</param>
    /// <returns>The pipeline-wrapped <see cref="IChatClient"/>.</returns>
    public IChatClient Wrap(IChatClient raw, string? modelLabel = null) =>
        Pipeline(Instrument(raw, string.IsNullOrWhiteSpace(modelLabel) ? "scripted" : modelLabel));

    // Per model, so OpenTelemetry tags each call with the model that actually answered.
    private static IChatClient Instrument(IChatClient raw, string model) =>
        raw.AsBuilder()
            .Use(inner => new ModelUsageChatClient(inner, model))
            .UseOpenTelemetry()
            .Build();

    private static IChatClient Pipeline(IChatClient model) =>
        model.AsBuilder()
            // Outermost: strip the tools the caller disabled for this run before function invocation or
            // the model ever see them. The agent framework only unions per-run tools, so restricting to a
            // subset of an agent's tools must happen here rather than through run options.
            .Use(inner => new ToolFilteringChatClient(inner))
            .UseFunctionInvocation(configure: client => client.FunctionInvoker = FlowExecutionScope.InvokeFunctionAsync)
            .Use(inner => new CapturingChatClient(inner))
            .Build();
}
