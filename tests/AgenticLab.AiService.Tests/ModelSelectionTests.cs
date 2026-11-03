using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.AiService.Application.Models;
using AgenticLab.AiService.Endpoints;
using AgenticLab.AiService.Startup;
using AgenticLab.ModelProviders;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ModelSelectionTests
{
    [Fact]
    public void ChoicesKeepConfiguredOrderAndNamesWithTheProviderDefault()
    {
        var configuration = Configuration("AzureOpenAI", "gpt-5.6-luna", ("gpt-5.6-luna", "Luna"), ("gpt-4o", null));

        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal([new ModelChoice("gpt-5.6-luna", "Luna"), new ModelChoice("gpt-4o", "gpt-4o")], choices.All);
        Assert.Equal("gpt-5.6-luna", choices.Default.Id);
    }

    [Fact]
    public void WithoutChoicesOnlyTheDefaultIsSelectable()
    {
        var configuration = Configuration("OpenAI", "test-model");

        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal("test-model", Assert.Single(choices.All).Id);
        Assert.False(choices.TryResolve("other", out _));
    }

    [Theory]
    [InlineData("missing-default")]
    [InlineData("duplicate")]
    [InlineData("missing-id")]
    public void InvalidChoicesFailAtStartup(string problem)
    {
        var configuration = problem switch
        {
            "missing-default" => Configuration("AzureOpenAI", "gpt-5.6-luna", ("gpt-4o", null)),
            "duplicate" => Configuration("AzureOpenAI", "gpt-4o", ("gpt-4o", null), ("GPT-4O", null)),
            _ => Configuration("AzureOpenAI", "gpt-4o", ("gpt-4o", null), (" ", "Blank")),
        };

        var error = Assert.Throws<InvalidOperationException>(() => new ModelChoices(configuration, new ModelConnectionOptions(configuration)));

        Assert.Contains(ModelChoices.SectionName, error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Theory]
    [InlineData("AzureOpenAI", " GPT-4O ", true, "gpt-4o")]
    [InlineData("OpenAI", "GPT-4O", false, null)]
    [InlineData("OpenAI", "gpt-4o", true, "gpt-4o")]
    [InlineData("AzureOpenAI", null, true, null)]
    [InlineData("AzureOpenAI", " ", true, null)]
    [InlineData("AzureOpenAI", "gpt-unknown", false, null)]
    public void RequestsResolveOnlyToAllowListedModels(string provider, string? requested, bool valid, string? expected)
    {
        var configuration = Configuration(provider, "gpt-5.6-luna", ("gpt-5.6-luna", null), ("gpt-4o", null));
        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal(valid, choices.TryResolve(requested, out var model));
        Assert.Equal(expected, model);
    }

    [Fact]
    public void SelectedModelWinsOverDeclaredAndForcedModelsButNotScriptedOnes()
    {
        var configuration = Configuration("AzureOpenAI", "gpt-5.6-luna", ("gpt-5.6-luna", null), ("gpt-4o", null));
        configuration["Models:ForceDefaultModel"] = "true";
        var clients = new ChatClientProvider(configuration);
        var agent = clients.Get(clients.ExecutionDeployment("declared-model"));
        using var scripted = clients.Wrap(new ScriptedModel(), "scripted-scenario");

        Assert.Equal("gpt-5.6-luna", ModelOf(agent));
        using (ModelSelectionScope.Begin("gpt-4o"))
        {
            Assert.Equal("gpt-4o", ModelOf(agent));
            Assert.Equal("scripted", ModelOf(scripted));
        }
        Assert.Equal("gpt-5.6-luna", ModelOf(agent));
    }

    [Fact]
    public async Task StreamingRunsRecordTheAnsweringModelTokensAndToolCalls()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var clients = new ChatClientProvider(Configuration("OpenAI", "test-model"));
        using var model = new ScriptedModel();
        var tool = AIFunctionFactory.Create(() => 1, "Count");
        var agent = new ChatClientAgent(clients.Wrap(model, "scripted-scenario"), instructions: "Test", name: "Test", tools: [tool]);
        var session = await agent.CreateSessionAsync(timeout.Token);
        using var scopes = RunScopeSet.Begin(null, null, null, null, interactive: false, model: "test-model");

        await foreach (var _ in agent.RunStreamingAsync("Count twice", session, cancellationToken: timeout.Token))
        {
            scopes.Activate();
        }

        var run = scopes.Model.Usage.Summarize(TimeSpan.FromMilliseconds(1500));
        Assert.Equal(new ModelRunSummary("scripted-scenario", 1500, 30, 7, 2, 2), run);
    }

    [Fact]
    public async Task EndpointsListModelsAndRejectAnyOtherModel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddConfiguration(Configuration("AzureOpenAI", "gpt-5.6-luna", ("gpt-5.6-luna", "Luna"), ("gpt-4o", "GPT-4o")));
        builder.Services.AddHarnessTools().AddWorkspaceFeatures(builder.Configuration).AddVendorHarnesses()
            .AddDemoAgents(builder.Configuration).AddConversationMemory(builder.Configuration)
            .AddChatInputLimits(builder.Configuration).AddFlowTracing().AddDiscovery();
        await using var app = builder.Build();
        app.MapAgentEndpoints();
        app.MapChatEndpoints();
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };

        using var models = JsonDocument.Parse(await client.GetStringAsync("/models", timeout.Token));
        Assert.Equal("gpt-5.6-luna", models.RootElement.GetProperty("default").GetString());
        Assert.Equal(["Luna", "GPT-4o"], models.RootElement.GetProperty("models").EnumerateArray().Select(model => model.GetProperty("name").GetString()));
        Assert.DoesNotContain("example.openai.azure.com", models.RootElement.GetRawText());
        Assert.DoesNotContain("test-key", models.RootElement.GetRawText());

        using var chat = await client.PostAsJsonAsync("/chat", new { message = "Hi", model = "gpt-5" }, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, chat.StatusCode);
        Assert.Contains("Unknown model 'gpt-5'", await chat.Content.ReadAsStringAsync(timeout.Token));
        using var stream = await client.PostAsJsonAsync("/chat/stream",
            new { message = "Hi", sessionId = "s", conversationId = "c", model = "gpt-5" }, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, stream.StatusCode);
    }

    private static string? ModelOf(IChatClient client) => client.GetService<ChatClientMetadata>()?.DefaultModelId;

    private static IConfigurationRoot Configuration(string provider, string defaultModel, params (string Id, string? Name)[] choices)
    {
        var values = new Dictionary<string, string?>
        {
            ["Models:Provider"] = provider,
            [$"{provider}:ApiKey"] = "test-key",
            [$"{provider}:{(provider == "AzureOpenAI" ? "Deployment" : "Model")}"] = defaultModel,
            [$"{provider}:Endpoint"] = "https://example.openai.azure.com/",
        };
        for (var i = 0; i < choices.Length; i++)
        {
            values[$"Models:Choices:{i}:Id"] = choices[i].Id;
            values[$"Models:Choices:{i}:Name"] = choices[i].Name;
        }
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    // Requests two tool calls, then answers, reporting usage the way the OpenAI adapter does when streaming.
    private sealed class ScriptedModel : IChatClient
    {
        private int _requests;

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            var first = Interlocked.Increment(ref _requests) == 1;
            yield return first
                ? new ChatResponseUpdate(ChatRole.Assistant, [new FunctionCallContent("one", "Count"), new FunctionCallContent("two", "Count")])
                    { FinishReason = ChatFinishReason.ToolCalls }
                : new ChatResponseUpdate(ChatRole.Assistant, "Done") { FinishReason = ChatFinishReason.Stop };
            yield return new ChatResponseUpdate(ChatRole.Assistant,
                [new UsageContent(new UsageDetails { InputTokenCount = first ? 10 : 20, OutputTokenCount = first ? 5 : 2 })]);
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata(defaultModelId: "scripted") : null;

        public void Dispose() { }
    }
}
