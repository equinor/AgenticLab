using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Agents.AI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.AiService.Application.Models;
using AgenticLab.AiService.Endpoints;
using AgenticLab.AiService.Startup;
using AgenticLab.Examples.Security.Agents;
using AgenticLab.Examples.Security.Tools;
using AgenticLab.ModelProviders;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ModelSelectionTests
{
    [Fact]
    public void ChoicesKeepConfiguredOrderAndNamesWithTheProviderDefault()
    {
        var configuration = Configuration("AzureOpenAI", "gpt-5.6-luna", " gpt-5.6-luna = Luna ,gpt-4o, ");

        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal([new ModelChoice("gpt-5.6-luna", "Luna"), new ModelChoice("gpt-4o", "GPT-4o")], choices.All);
        Assert.Equal("gpt-5.6-luna", choices.Default.Id);
    }

    [Fact]
    public void WithoutChoicesOnlyTheDefaultIsSelectable()
    {
        var configuration = Configuration("OpenAI", "test-model");

        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal(new ModelChoice("test-model", "Test Model"), Assert.Single(choices.All));
        Assert.False(choices.TryResolve("other", out _));
    }

    [Fact]
    public void AnUnlistedDefaultIsAddedFirst()
    {
        var configuration = Configuration("AzureOpenAI", "my-deployment", "gpt-4o");

        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal(["my-deployment", "gpt-4o"], choices.All.Select(choice => choice.Id));
        Assert.Equal("my-deployment", choices.Default.Id);
    }

    [Theory]
    [InlineData("gpt-4o, GPT-4O")]
    [InlineData("gpt-4o, =Blank")]
    public void InvalidChoicesFailAtStartup(string list)
    {
        var configuration = Configuration("AzureOpenAI", "gpt-4o", list);

        var error = Assert.Throws<InvalidOperationException>(() => new ModelChoices(configuration, new ModelConnectionOptions(configuration)));

        Assert.Contains("AzureOpenAI:Choices", error.Message);
        Assert.DoesNotContain("test-key", error.Message);
    }

    [Theory]
    [InlineData("gpt-5.6-luna", "GPT-5.6 Luna")]
    [InlineData("gpt-4o", "GPT-4o")]
    [InlineData("gemini-2.5-flash", "Gemini 2.5 Flash")]
    [InlineData("o3", "O3")]
    public void DisplayNamesAreDerivedFromIds(string id, string expected) =>
        Assert.Equal(expected, ModelChoices.DisplayName(id));

    [Fact]
    public void ShippedDefaultsAreOverridableAndApplyOnlyToAzure()
    {
        var credentials = new Dictionary<string, string?>
        {
            ["AzureOpenAI:Endpoint"] = "https://example.openai.azure.com/",
            ["AzureOpenAI:ApiKey"] = "test-key",
        };
        var shipped = new ConfigurationBuilder().AddInMemoryCollection(credentials).AddModelDefaults().Build();
        var choices = new ModelChoices(shipped, new ModelConnectionOptions(shipped));

        Assert.Equal(["GPT-5.6 Luna", "GPT-5.6 Terra", "GPT-5.6 Sol", "GPT-4o"], choices.All.Select(choice => choice.Name));
        Assert.Equal("gpt-5.6-luna", choices.Default.Id);

        credentials["AzureOpenAI:Choices"] = "gpt-4o";
        credentials["AzureOpenAI:Deployment"] = "gpt-4o";
        var overridden = new ConfigurationBuilder().AddInMemoryCollection(credentials).AddModelDefaults().Build();
        Assert.Equal("gpt-4o", Assert.Single(new ModelChoices(overridden, new ModelConnectionOptions(overridden)).All).Id);

        var openAi = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Models:Provider"] = "OpenAI",
            ["OpenAI:ApiKey"] = "test-key",
            ["OpenAI:Model"] = "native-model",
        }).AddModelDefaults().Build();
        Assert.Equal("native-model", Assert.Single(new ModelChoices(openAi, new ModelConnectionOptions(openAi)).All).Id);
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
        var configuration = Configuration(provider, "gpt-5.6-luna", "gpt-5.6-luna, gpt-4o");
        var choices = new ModelChoices(configuration, new ModelConnectionOptions(configuration));

        Assert.Equal(valid, choices.TryResolve(requested, out var model));
        Assert.Equal(expected, model);
    }

    [Fact]
    public void SelectedModelWinsOverDeclaredAndForcedModelsButNotScriptedOnes()
    {
        var configuration = Configuration("AzureOpenAI", "gpt-5.6-luna", "gpt-5.6-luna, gpt-4o");
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
        Assert.Equal(new ModelRunSummary("scripted-scenario", 1500, 30, 7, 2, 2), run with { ModelDurationMs = 0 });
        Assert.InRange(run.ModelDurationMs, 0, 1500);
    }

    [Fact]
    public async Task CapturedRequestsNameTheRoutedModelAndTimeOnlyTheModel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var client = new CapturingChatClient(new ModelRoutingChatClient("default-model",
            () => ModelSelectionScope.Current?.Model, id => new NamedModel(id)));
        using var capture = FlowCaptureScope.Begin();
        using var usage = ModelSelectionScope.Begin("gpt-4o");

        await foreach (var _ in client.GetStreamingResponseAsync([new(ChatRole.User, "Hi")], cancellationToken: timeout.Token))
        {
            // A paused consumer (manual stepping, breakpoints) must not count as model time.
            await Task.Delay(300, timeout.Token);
        }

        var turn = Assert.Single(capture.Turns);
        Assert.Contains("\"model\": \"gpt-4o\"", turn.RequestData);
        Assert.InRange(turn.ElapsedMilliseconds ?? -1, 0, 250);
    }

    [Fact]
    public async Task EndpointsListModelsAndRejectAnyOtherModel()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddConfiguration(Configuration("AzureOpenAI", "gpt-5.6-luna", "gpt-5.6-luna=Luna, gpt-4o"));
        builder.Services.AddHarnessTools().AddWorkspaceFeatures(builder.Configuration).AddVendorHarnesses()
            .AddDemoAgents(builder.Configuration).AddConversationMemory(builder.Configuration)
            .AddChatInputLimits(builder.Configuration).AddFlowTracing().AddDiscovery();
        builder.Services.AddSingleton<IAgentDefinition>(new InjectedBrowserAgent(new SimulatedWebTool()));
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

        // A scripted agent never runs on a provider model, so an explicit choice is rejected rather than ignored.
        using var scriptedChat = await client.PostAsJsonAsync("/chat",
            new { message = "Hi", agent = InjectedBrowserAgent.AgentName, model = "gpt-4o" }, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, scriptedChat.StatusCode);
        Assert.Contains("scripted model", await scriptedChat.Content.ReadAsStringAsync(timeout.Token));
        using var scriptedStream = await client.PostAsJsonAsync("/chat/stream",
            new { message = "Hi", agent = InjectedBrowserAgent.AgentName, sessionId = "s2", conversationId = "c2", model = "gpt-4o" }, timeout.Token);
        Assert.Equal(HttpStatusCode.BadRequest, scriptedStream.StatusCode);
    }

    /// <summary>The stream reports each round-trip's own tokens on its response; together they are the run's totals.</summary>
    [Fact]
    public async Task StreamedResponsesCarryTheirOwnUsageWhichSumsToTheRunSummary()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration.AddConfiguration(Configuration("OpenAI", "test-model"));
        builder.Services.AddHarnessTools().AddWorkspaceFeatures(builder.Configuration).AddVendorHarnesses()
            .AddDemoAgents(builder.Configuration).AddConversationMemory(builder.Configuration)
            .AddChatInputLimits(builder.Configuration).AddFlowTracing().AddDiscovery();
        builder.Services.AddSingleton<IAgentDefinition>(new UsageReportingAgent());
        await using var app = builder.Build();
        app.MapChatEndpoints();
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };

        using var response = await client.PostAsJsonAsync("/chat/stream",
            new { message = "Count twice", agent = UsageReportingAgent.AgentName, sessionId = "usage", conversationId = "usage" }, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var events = (await response.Content.ReadAsStringAsync(timeout.Token)).Split('\n')
            .Where(line => line.StartsWith("data: ", StringComparison.Ordinal))
            .Select(line => JsonDocument.Parse(line["data: ".Length..]).RootElement)
            .ToArray();

        static bool Is(JsonElement step, string kind) => step.GetProperty("kind").GetString() == kind;
        static long? Cached(JsonElement usage) =>
            usage.TryGetProperty("cachedInputTokens", out var cached) && cached.ValueKind == JsonValueKind.Number ? cached.GetInt64() : null;
        // The first call reports no cache usage; the second reads 16 of its 20 input tokens from the cache.
        Assert.Equal([(10L, 5L, (long?)null), (20L, 2L, 16L)], events.Where(step => Is(step, "llm-response"))
            .Select(step => step.GetProperty("usage"))
            .Select(usage => (usage.GetProperty("inputTokens").GetInt64(), usage.GetProperty("outputTokens").GetInt64(), Cached(usage))));
        Assert.All(events.Where(step => !Is(step, "llm-response")), step =>
            Assert.True(!step.TryGetProperty("usage", out var usage) || usage.ValueKind == JsonValueKind.Null));
        var run = events.Single(step => Is(step, "final")).GetProperty("run");
        Assert.Equal(30, run.GetProperty("inputTokens").GetInt64());
        Assert.Equal(7, run.GetProperty("outputTokens").GetInt64());
    }

    private static string? ModelOf(IChatClient client) => client.GetService<ChatClientMetadata>()?.DefaultModelId;

    private static IConfigurationRoot Configuration(string provider, string defaultModel, string? choices = null) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Models:Provider"] = provider,
            [$"{provider}:ApiKey"] = "test-key",
            [$"{provider}:{(provider == "AzureOpenAI" ? "Deployment" : "Model")}"] = defaultModel,
            [$"{provider}:Endpoint"] = "https://example.openai.azure.com/",
            [$"{provider}:Choices"] = choices,
        }).Build();

    // Answers in two updates and reports the model id it was created for, like a provider client.
    private sealed class NamedModel(string id) : IChatClient
    {
        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, "Do");
            yield return new ChatResponseUpdate(ChatRole.Assistant, "ne") { FinishReason = ChatFinishReason.Stop };
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata(defaultModelId: id) : null;

        public void Dispose() { }
    }

    // A scripted agent on the usage-reporting model, so a whole stream can be read without a provider.
    private sealed class UsageReportingAgent : AgentDefinitionBase, IScriptedModelAgent
    {
        public const string AgentName = "UsageReporting";

        public override string Name => AgentName;
        public override string Description => "Counts twice, then answers.";
        protected override string Persona => "Count twice.";
        public override IList<AITool> Tools { get; } = [AIFunctionFactory.Create(() => 1, "Count")];
        public IChatClient CreateModel() => new ScriptedModel();
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
                [new UsageContent(new UsageDetails { InputTokenCount = first ? 10 : 20, OutputTokenCount = first ? 5 : 2, CachedInputTokenCount = first ? null : 16 })]);
        }

        public Task<Microsoft.Extensions.AI.ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) =>
            serviceType == typeof(ChatClientMetadata) ? new ChatClientMetadata(defaultModelId: "scripted") : null;

        public void Dispose() { }
    }
}
