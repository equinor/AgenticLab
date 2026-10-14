using System.ClientModel.Primitives;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Agents.AI.Hosting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AgenticLab.AiService.Application.Discovery;
using AgenticLab.Extensibility.Examples;
using AgenticLab.McpServer.Tools;
using AgenticLab.ModelProviders;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class ProtocolIntegrationTests
{
    [Fact]
    public async Task McpDiscoveryAndRediscoveryExposeCallableTimeToolOverHttp()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = CreateBuilder();
        builder.Services.AddMcpServer().WithHttpTransport().WithTools<TimeTools>();
        await using var server = builder.Build();
        server.MapMcp();
        await server.StartAsync(timeout.Token);
        var configuration = EndpointConfiguration("Mcp:Endpoint", server);
        await using var provider = new McpToolProvider(configuration, NullLogger<McpToolProvider>.Instance);

        for (var discovery = 0; discovery < 2; discovery++)
        {
            await provider.ConnectAsync(timeout.Token);

            Assert.True(provider.IsConnected, provider.Status.Error);
            Assert.Equal(DiscoveryState.Connected, provider.Status.State);
            Assert.Equal("GetCurrentTime", Assert.Single(provider.ToolInfos).Name);
            Assert.Null(Assert.Single(provider.Status.Items).ExampleId);
            var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.GetTools()));
            var result = await tool.InvokeAsync(new AIFunctionArguments(), timeout.Token);
            Assert.Matches(@"\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}", JsonSerializer.Serialize(result));
        }

        await using var claimed = new McpToolProvider(configuration, NullLogger<McpToolProvider>.Instance,
            new ExampleCatalog([new ClaimingModule(mcpToolNames: ["GetCurrentTime"])]));
        await claimed.ConnectAsync(timeout.Token);
        Assert.Equal("claimer", Assert.Single(claimed.Status.Items).ExampleId);
        Assert.Single(claimed.GetTools(["GetCurrentTime"]));
    }

    [Theory]
    [InlineData("Fake")]
    [InlineData("AzureOpenAI")]
    [InlineData("OpenAI")]
    [InlineData("Gemini")]
    public async Task A2ADiscoveryAndDelegationReturnTheHostedAgentsAnswerOverHttp(string modelProvider)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var builder = CreateBuilder();
        using var handler = new EchoProviderHandler();
        using var httpClient = new HttpClient(handler);
        var modelConfiguration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Models:Provider"] = modelProvider,
            [$"{modelProvider}:ApiKey"] = "test-key",
            [$"{modelProvider}:{(modelProvider == "AzureOpenAI" ? "Deployment" : "Model")}"] = "test-model",
            [$"{modelProvider}:Endpoint"] = "https://example.openai.azure.com/",
        }).Build();
        IChatClient model = modelProvider == "Fake" ? new EchoModel()
            : new ModelClientFactory(new ModelConnectionOptions(modelConfiguration), new HttpClientPipelineTransport(httpClient))
                .Create().AsBuilder().UseFunctionInvocation().UseOpenTelemetry().Build();
        builder.Services.AddSingleton<IChatClient>(model);
        var agent = builder.AddAIAgent("research", instructions: "Echo the question.");
        agent.AddA2AServer();
        var specialist = builder.AddAIAgent("windfarm-risk", instructions: "Echo the question.");
        specialist.AddA2AServer();
        await using var server = builder.Build();
        server.MapA2AJsonRpc(agent, "/a2a/research");
        server.MapA2AJsonRpc(specialist, "/a2a/windfarm-risk");
        server.MapGet("/agents", () => Results.Ok(new
        {
            Agents = new[]
            {
                new { Name = "research", Path = "/a2a/research", Description = "Test specialist" },
                new { Name = "windfarm-risk", Path = "/a2a/windfarm-risk", Description = "Claimed specialist" },
            },
        }));
        await server.StartAsync(timeout.Token);
        var configuration = EndpointConfiguration("A2A:Endpoint", server);
        var examples = new ExampleCatalog([new ClaimingModule(remoteAgentNames: ["windfarm-risk"])]);
        using var provider = new A2AAgentProvider(configuration, NullLogger<A2AAgentProvider>.Instance, examples);

        for (var discovery = 0; discovery < 2; discovery++)
        {
            await provider.ConnectAsync(timeout.Token);

            Assert.True(provider.IsConnected, provider.Status.Error);
            Assert.Equal(DiscoveryState.Connected, provider.Status.State);
            Assert.Equal(new[] { "research", "windfarm-risk" }, provider.AgentInfos.Select(info => info.Name));
            Assert.Equal(new[] { "research" }, provider.SharedAgentNames);
            Assert.Equal(new string?[] { null, "claimer" }, provider.Status.Items.Select(item => item.ExampleId));
            var tool = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.GetTools()));
            Assert.Contains("research", tool.Description);
            Assert.DoesNotContain("windfarm-risk", tool.Description);
            var result = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["agentName"] = "research",
                ["question"] = "What is the test answer?",
            }, timeout.Token);
            Assert.Equal("Answer: What is the test answer?", result?.ToString());
            var refused = await tool.InvokeAsync(new AIFunctionArguments
            {
                ["agentName"] = "windfarm-risk",
                ["question"] = "Should not be sent.",
            }, timeout.Token);
            Assert.Equal("Unknown agent 'windfarm-risk'. Available agents: research.", refused?.ToString());
        }
        Assert.Equal(modelProvider == "Fake" ? 0 : 2, handler.RequestCount);

        var bounded = Assert.IsAssignableFrom<AIFunction>(Assert.Single(provider.GetTools(["windfarm-risk"])));
        Assert.DoesNotContain("research", bounded.Description);
        var delegated = await bounded.InvokeAsync(new AIFunctionArguments
        {
            ["agentName"] = "windfarm-risk",
            ["question"] = "Review this.",
        }, timeout.Token);
        Assert.Equal("Answer: Review this.", delegated?.ToString());
        Assert.Empty(provider.GetTools(["unknown"]));
        Assert.True((await provider.InvokeAsync("windfarm-risk", "Direct.", timeout.Token)).Success);
    }

    // A stand-in enabled example claiming protocol names, so ownership exclusion can be tested without a host.
    private sealed class ClaimingModule(string[]? mcpToolNames = null, string[]? remoteAgentNames = null) : IExampleModule
    {
        public ExampleManifest Manifest { get; } = new("claimer", "Claimer", ["claimer"], [])
        {
            McpToolNames = mcpToolNames ?? [],
            RemoteAgentNames = remoteAgentNames ?? [],
        };
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        return builder;
    }

    private static IConfiguration EndpointConfiguration(string key, WebApplication server) =>
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            [key] = Assert.Single(server.Urls),
        }).Build();

    private sealed class EchoProviderHandler : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            var content = root.GetProperty("messages").EnumerateArray()
                .Last(message => message.GetProperty("role").GetString() == "user").GetProperty("content");
            var question = content.ValueKind == JsonValueKind.String ? content.GetString()
                : string.Concat(content.EnumerateArray().Select(part => part.GetProperty("text").GetString()));
            var answer = $"Answer: {question}";
            var streaming = root.TryGetProperty("stream", out var stream) && stream.GetBoolean();
            var response = streaming
                ? "data: " + JsonSerializer.Serialize(new
                {
                    id = "reply", @object = "chat.completion.chunk", created = 1, model = "test-model",
                    choices = new[] { new { index = 0, delta = new { role = "assistant", content = answer }, finish_reason = "stop" } },
                }) + "\n\ndata: [DONE]\n\n"
                : JsonSerializer.Serialize(new
                {
                    id = "reply", @object = "chat.completion", created = 1, model = "test-model",
                    choices = new[] { new { index = 0, message = new { role = "assistant", content = answer }, finish_reason = "stop" } },
                });
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(response, Encoding.UTF8, streaming ? "text/event-stream" : "application/json"),
            };
        }
    }

    private sealed class EchoModel : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer(messages)))
            {
                ResponseId = Guid.NewGuid().ToString(),
                FinishReason = ChatFinishReason.Stop,
            });
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, Answer(messages))
            {
                ResponseId = Guid.NewGuid().ToString(),
                FinishReason = ChatFinishReason.Stop,
            };
        }

        private static string Answer(IEnumerable<ChatMessage> messages) =>
            $"Answer: {messages.Last(message => message.Role == ChatRole.User).Text}";

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose() { }
    }
}