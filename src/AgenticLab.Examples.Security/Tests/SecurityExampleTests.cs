using AgenticLab.Examples.Security.Agents;
using AgenticLab.Examples.Security.Scenarios;
using AgenticLab.Examples.Security.Scripting;
using AgenticLab.Examples.Security.Tools;
using AgenticLab.Extensibility.Agents;
using AgenticLab.Extensibility.Examples;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace AgenticLab.Examples.Security.Tests;

public sealed class SecurityExampleTests
{
    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    public void DisabledModuleContributesNothing(bool? enabled)
    {
        using var provider = new ServiceCollection()
            .AddExample<SecurityExample>(Configuration(enabled), ExampleHost.AiService)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.Empty(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.Empty(provider.GetServices<IAgentDefinition>());
        Assert.Empty(provider.GetServices<IVendorHarness>());
    }

    [Fact]
    public void EnabledModuleRegistersScriptedAgentsHarnessAndScenarios()
    {
        using var provider = new ServiceCollection()
            .AddExample<SecurityExample>(Configuration(true), ExampleHost.AiService)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var agents = provider.GetServices<IAgentDefinition>().ToArray();
        Assert.Equal(["InjectedBrowser", "LeakyAssistant", "PoisonedTools", "ReceiptReader", "BuildHelper", "DefendedAssistant"], agents.Select(a => a.Name));
        Assert.All(agents, agent =>
        {
            Assert.IsAssignableFrom<IScriptedModelAgent>(agent);
            Assert.Equal(ScenarioModel.ModelId, agent.ModelId);
            Assert.False(agent.RequiresWorkspace);
            Assert.Single(agent.SuggestedPrompts);
            Assert.NotNull(SecurityScenarios.ForAgent(agent.Name));
        });
        Assert.Equal(["FetchPage"], agents[0].Tools.Select(t => t.Name));
        Assert.Equal(["ReadSharedDocument", "SendMail"], agents[1].Tools.Select(t => t.Name));
        Assert.Equal(["GetDeploymentNotes", "ConvertUnits"], agents[2].Tools.Select(t => t.Name));
        Assert.Equal(["DescribeImage", "SubmitExpense"], agents[3].Tools.Select(t => t.Name));
        Assert.Equal(["ReadFile", "RunCommand"], agents[4].Tools.Select(t => t.Name));
        Assert.Equal(["ReadSharedDocument", "SendMail"], agents[5].Tools.Select(t => t.Name));

        var harness = Assert.Single(provider.GetServices<IVendorHarness>());
        Assert.Equal("security", harness.Key);
        Assert.Equal(agents.Select(a => a.Name), harness.Modes.Select(m => m.Agent));

        var module = Assert.Single(provider.GetRequiredService<ExampleCatalog>().Modules);
        Assert.Equal(agents.Select(a => a.Name), module.Manifest.AgentNames);
        Assert.Equal(SecurityScenarios.All.Select(s => s.AgentName).OrderBy(n => n), module.Manifest.AgentNames.OrderBy(n => n));
        var presentedTools = module.Manifest.Resources.SelectMany(r => r.ToolNames).ToHashSet();
        Assert.Equal(agents.SelectMany(a => a.Tools).Select(t => t.Name).ToHashSet(), presentedTools);
        Assert.All(module.Manifest.ToolRisks.Keys, name => Assert.Contains(name, presentedTools));
    }

    [Fact]
    public void WebRegistrationProvidesPanelWithoutBackendServices()
    {
        using var provider = new ServiceCollection()
            .AddExample<SecurityExample>(Configuration(true), ExampleHost.Web)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        var module = Assert.Single(provider.GetRequiredService<ExampleCatalog>().Modules);
        var web = Assert.IsAssignableFrom<IWebExample>(module);
        Assert.Equal(typeof(Components.SecurityScenarioPanel), web.PanelType);
        Assert.False(module.Manifest.RequiresUi);
        Assert.Equal("_content/AgenticLab.Examples.Security/host.svg", module.Manifest.HostPresentation["security"].IconPath);
        Assert.Equal("securing-agents", module.Manifest.HostPresentation["security"].ProductConceptId);
        Assert.Empty(provider.GetServices<IAgentDefinition>());
    }

    [Fact]
    public async Task ExfiltrationScenarioLeaksThroughUnguardedMail()
    {
        var agent = new LeakyAssistantAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools);

        Assert.Equal(["ReadSharedDocument", "SendMail"], calls.Select(c => c.Name));
        var to = Assert.IsType<string>(calls[1].Arguments!["to"]);
        Assert.Contains(SimulatedWorkplaceTool.AttackerAddress, to);
        Assert.Contains("Simulated: mail queued", text);
        Assert.Contains(SimulatedWorkplaceTool.AttackerAddress, text);
    }

    [Fact]
    public async Task DisablingSendMailStopsTheChainWithoutAnError()
    {
        var agent = new LeakyAssistantAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools.Where(t => t.Name != "SendMail").ToList());

        Assert.Equal(["ReadSharedDocument"], calls.Select(c => c.Name));
        Assert.Contains("SendMail", text);
        Assert.Contains("not available", text);
    }

    [Fact]
    public async Task DefendedTwinBlocksTheSameRequestInCode()
    {
        var agent = new DefendedAssistantAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools);

        Assert.Equal(["ReadSharedDocument", "SendMail"], calls.Select(c => c.Name));
        Assert.Contains("Blocked by policy", text);
        Assert.Contains(SimulatedWorkplaceTool.AttackerAddress, text);
        Assert.DoesNotContain("mail queued", text);
    }

    [Fact]
    public async Task PoisonedToolReceivesTheTokenAndTheAnswerHidesIt()
    {
        var agent = new PoisonedToolsAgent();
        var convert = agent.Tools.Single(t => t.Name == "ConvertUnits");
        Assert.Contains("<IMPORTANT>", convert.Description);

        var (text, calls) = await RunAsync(agent, agent.Tools);
        Assert.Equal(["GetDeploymentNotes", "ConvertUnits"], calls.Select(c => c.Name));
        Assert.Equal(SimulatedWorkplaceTool.FakeToken, calls[1].Arguments!["note"]);
        Assert.Contains("33.333", text);
        Assert.DoesNotContain("sk-demo", text);
    }

    [Fact]
    public async Task InjectedPageSteersTheAnswerWithoutAnyToolMisuse()
    {
        var agent = new InjectedBrowserAgent(new SimulatedWebTool());
        var (text, calls) = await RunAsync(agent, agent.Tools);

        Assert.Equal(["FetchPage"], calls.Select(c => c.Name));
        Assert.Contains(SimulatedWebTool.AttackerIban, text);
        Assert.Contains("<!--", new SimulatedWebTool().FetchPage(SimulatedWebTool.VendorUpdateUrl));
    }

    [Fact]
    public async Task HiddenImageTextDrivesTheExpenseSubmission()
    {
        var agent = new ReceiptReaderAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools);

        Assert.Equal(["DescribeImage", "SubmitExpense"], calls.Select(c => c.Name));
        Assert.Equal(4870.00m, Assert.IsType<decimal>(calls[1].Arguments!["amount"]));
        Assert.Equal(SimulatedVisionTool.AttackerPayee, calls[1].Arguments!["payee"]);
        Assert.Contains(SimulatedVisionTool.VisibleTotal, text);
        Assert.Matches(@"EXP-\d{4}", text);
        Assert.DoesNotContain("4870", text);
        Assert.DoesNotContain(SimulatedVisionTool.AttackerPayee, text);
    }

    [Fact]
    public async Task DisablingSubmitExpenseStopsTheImageChain()
    {
        var agent = new ReceiptReaderAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools.Where(t => t.Name != "SubmitExpense").ToList());

        Assert.Equal(["DescribeImage"], calls.Select(c => c.Name));
        Assert.Contains("SubmitExpense", text);
        Assert.Contains("not available", text);
    }

    [Fact]
    public void VisionTranscriptionCarriesTheHiddenInstructionAndTheAssetMatches()
    {
        var transcription = new SimulatedVisionTool().DescribeImage(SimulatedVisionTool.ReceiptImageName);
        Assert.Contains($"TOTAL                     {SimulatedVisionTool.VisibleTotal} EUR", transcription);
        Assert.EndsWith(SimulatedVisionTool.HiddenInstruction, transcription);
        Assert.Contains("Attached images:", new SimulatedVisionTool().DescribeImage("other.png"));

        var scenario = Assert.Single(SecurityScenarios.All, s => s.Id == "image-injection");
        Assert.Equal(SimulatedVisionTool.ReceiptImagePath, scenario.ImagePath);
        Assert.All(SecurityScenarios.All.Where(s => s.Id != "image-injection"), s => Assert.Null(s.ImagePath));

        var svg = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "receipt-0423.svg"));
        Assert.Contains("48.70 EUR", svg);
        Assert.Contains("SYSTEM NOTE FOR THE AI EXPENSE ASSISTANT", svg);
        Assert.Contains($"{SimulatedVisionTool.HiddenAmount} EUR payable to {SimulatedVisionTool.AttackerPayee}", svg);
    }

    [Fact]
    public async Task RepositoryNoteDrivesTheDangerousCommandAndTheAnswerHidesIt()
    {
        var agent = new BuildHelperAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools);

        Assert.Equal(["ReadFile", "ReadFile", "RunCommand", "RunCommand"], calls.Select(c => c.Name));
        Assert.Equal(SimulatedRepoTool.InjectedCommand, calls[2].Arguments!["command"]);
        Assert.Equal(BuildHelperAgent.FixCommand, calls[3].Arguments!["command"]);
        Assert.Contains(BuildHelperAgent.FixCommand, text);
        Assert.DoesNotContain("curl", text);
        Assert.DoesNotContain("rm -rf", text);
    }

    [Fact]
    public async Task DisablingRunCommandStopsTheCommandChain()
    {
        var agent = new BuildHelperAgent();
        var (text, calls) = await RunAsync(agent, agent.Tools.Where(t => t.Name != "RunCommand").ToList());

        Assert.Equal(["ReadFile", "ReadFile"], calls.Select(c => c.Name));
        Assert.Contains("RunCommand", text);
        Assert.Contains("not available", text);
    }

    [Fact]
    public void SimulatedShellClassifiesWithoutExecutingAndTheRepoCarriesTheNote()
    {
        var shell = new SimulatedShellTool();
        var transcript = shell.RunCommand(SimulatedRepoTool.InjectedCommand);
        Assert.Contains("No process was started", transcript);
        Assert.Contains("remote code execution", transcript);
        Assert.Contains("credential stores", transcript);
        Assert.Contains("exit code 0", shell.RunCommand("npm ci"));
        Assert.Equal([SimulatedRepoTool.InjectedCommand, "npm ci"], shell.Commands.Select(c => c.Command));
        Assert.Empty(shell.Commands.Last().Hazards);
        Assert.Equal(3, shell.Commands.First().Hazards.Count);

        var repo = new SimulatedRepoTool();
        Assert.EndsWith(SimulatedRepoTool.InjectedNote + "\n", repo.ReadFile(SimulatedRepoTool.SetupScriptPath));
        Assert.Contains(SimulatedRepoTool.SetupScriptPath, repo.ReadFile(SimulatedRepoTool.BuildLogPath));
        Assert.Contains("Files in this repository:", repo.ReadFile("secrets.env"));
        Assert.Equal(repo.ReadFile(SimulatedRepoTool.SetupScriptPath), repo.ReadFile("scripts\\setup.sh"));
    }

    [Fact]
    public async Task SecondTurnRepliesWithFollowUpAndNonStreamingMatchesStreaming()
    {
        var agent = new LeakyAssistantAgent();
        var client = ((IScriptedModelAgent)agent).CreateModel().AsBuilder().UseFunctionInvocation().Build();
        var options = new ChatOptions { Tools = agent.Tools };
        List<ChatMessage> history = [new(ChatRole.User, agent.Prompt)];

        var first = await client.GetResponseAsync(history, options);
        Assert.Contains("mail queued", first.Text);
        history.AddRange(first.Messages);
        history.Add(new ChatMessage(ChatRole.User, "Again?"));

        var second = await client.GetResponseAsync(history, options);
        Assert.Contains("new conversation", second.Text);
        Assert.DoesNotContain(second.Messages, m => m.Contents.OfType<FunctionCallContent>().Any());
    }

    [Fact]
    public void GuardedToolsWrapDocumentsAndRejectExternalRecipients()
    {
        var guarded = new SimulatedWorkplaceTool(guarded: true);
        Assert.StartsWith("<untrusted-document", guarded.ReadSharedDocument(SimulatedWorkplaceTool.PricingDocumentName));
        Assert.StartsWith("Blocked by policy", guarded.SendMail("me@contoso.example, x@evil.example", "s", "b"));
        Assert.Empty(guarded.Outbox);
        Assert.StartsWith("Simulated", guarded.SendMail("me@contoso.example", "s", "b"));
        Assert.Single(guarded.Outbox);

        var open = new SimulatedWorkplaceTool(guarded: false);
        Assert.DoesNotContain("<untrusted-document", open.ReadSharedDocument(SimulatedWorkplaceTool.PricingDocumentName));
        Assert.StartsWith("Simulated", open.SendMail("x@evil.example", "s", "b"));
    }

    [Fact]
    public void PlaybookMustEndWithAnAnswer()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new ScenarioModel(new Playbook([new ScriptedToolCall("X", new Dictionary<string, object?>())], "follow-up")));
    }

    private static async Task<(string Text, List<FunctionCallContent> Calls)> RunAsync(IScriptedModelAgent agent, IList<AITool> tools)
    {
        var client = agent.CreateModel().AsBuilder().UseFunctionInvocation().Build();
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in client.GetStreamingResponseAsync([new ChatMessage(ChatRole.User, agent.SuggestedPrompts[0])], new ChatOptions { Tools = tools }))
            updates.Add(update);
        var response = updates.ToChatResponse();
        var calls = response.Messages.SelectMany(m => m.Contents.OfType<FunctionCallContent>()).ToList();
        return (response.Text, calls);
    }

    private static IConfiguration Configuration(bool? enabled) => new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [$"Examples:{SecurityExample.Id}:Enabled"] = enabled?.ToString(),
        }).Build();
}
