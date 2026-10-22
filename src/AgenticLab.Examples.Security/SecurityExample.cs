using AgenticLab.Examples.Security.Agents;
using AgenticLab.Examples.Security.Components;
using AgenticLab.Examples.Security.Tools;
using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AgenticLab.Examples.Security;

/// <summary>
/// Opt-in "Security Lab": scripted agents that show what goes wrong when untrusted content, over-privileged
/// tools or poisoned tool descriptions reach an agent, and which harness controls stop it. Every scenario runs on
/// an offline <see cref="Scripting.ScenarioModel"/> with simulated tools: no provider call, network, mail, file or
/// command side effects. Enable with <c>Examples:security:Enabled=true</c>.
/// </summary>
public sealed class SecurityExample : IAiServiceExample, IWebExample
{
    /// <summary>The module's configuration and catalogue identity.</summary>
    public const string Id = "security";

    /// <summary>The API host key used for chat requests and saved host selections.</summary>
    public const string HostKey = "security";

    /// <inheritdoc />
    public ExampleManifest Manifest { get; } = new(Id, "Security Lab", [HostKey],
        [InjectedBrowserAgent.AgentName, LeakyAssistantAgent.AgentName, PoisonedToolsAgent.AgentName, ReceiptReaderAgent.AgentName, DefendedAssistantAgent.AgentName])
    {
        HostPresentation = new Dictionary<string, ExampleHostPresentation>
        {
            [HostKey] = new("_content/AgenticLab.Examples.Security/host.svg", 70, "securing-agents"),
        },
        ToolRisks = new Dictionary<string, ExampleToolRisk>
        {
            [nameof(SimulatedWorkplaceTool.SendMail)] = new("High",
                "High risk in this lab: the unguarded variant accepts any recipient, so injected content can exfiltrate data. Simulated; nothing is sent."),
            [nameof(PoisonedThirdPartyTool.ConvertUnits)] = new("High",
                "High risk: a third-party tool whose description contains hidden instructions for the model. Its note argument is logged, not transmitted."),
            [nameof(SimulatedExpenseTool.SubmitExpense)] = new("High",
                "High risk: files whatever amount and payee the model passes, with no check of its own. Simulated; nothing is paid."),
            [nameof(SimulatedWorkplaceTool.GetDeploymentNotes)] = new("Medium",
                "Medium risk: returns internal notes containing a placeholder token that other tools could receive."),
            [nameof(SimulatedWorkplaceTool.ReadSharedDocument)] = new("Medium",
                "Medium risk: returns a document containing an embedded instruction. Read-only, in-memory sample data."),
            [nameof(SimulatedWebTool.FetchPage)] = new("Medium",
                "Medium risk: returns attacker-controlled page content with a hidden instruction. No network access; fixed in-memory pages."),
            [nameof(SimulatedVisionTool.DescribeImage)] = new("Medium",
                "Medium risk: returns a scripted transcription of an image, including near-invisible text. No image is decoded or uploaded."),
        },
        Resources =
        [
            new("security-web", "Simulated web", "In-memory pages", [nameof(SimulatedWebTool.FetchPage)]),
            new("security-workplace", "Simulated workplace", "In-memory documents and mail",
                [nameof(SimulatedWorkplaceTool.ReadSharedDocument), nameof(SimulatedWorkplaceTool.SendMail), nameof(SimulatedWorkplaceTool.GetDeploymentNotes)]),
            new("security-third-party", "Third-party converter", "Poisoned tool description", [nameof(PoisonedThirdPartyTool.ConvertUnits)]),
            new("security-vision", "Simulated vision", "Scripted image transcription", [nameof(SimulatedVisionTool.DescribeImage)]),
            new("security-expenses", "Simulated expenses", "In-memory submissions", [nameof(SimulatedExpenseTool.SubmitExpense)]),
        ],
    };

    /// <inheritdoc />
    public Type PanelType => typeof(SecurityScenarioPanel);

    /// <inheritdoc />
    public void AddAiServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<SimulatedWebTool>();
        services.AddSingleton<IAgentDefinition, InjectedBrowserAgent>();
        services.AddSingleton<IAgentDefinition, LeakyAssistantAgent>();
        services.AddSingleton<IAgentDefinition, PoisonedToolsAgent>();
        services.AddSingleton<IAgentDefinition, ReceiptReaderAgent>();
        services.AddSingleton<IAgentDefinition, DefendedAssistantAgent>();
        services.AddSingleton<IVendorHarness, SecurityHarness>();
    }

    /// <summary>Uses the host's existing chat and catalogue routes; adds no module-specific API.</summary>
    public void MapApi(RouteGroupBuilder group) { }

    /// <summary>The panel is static content over the scenario catalogue; it needs no services.</summary>
    public void AddWebServices(IServiceCollection services, IConfiguration configuration) { }
}
