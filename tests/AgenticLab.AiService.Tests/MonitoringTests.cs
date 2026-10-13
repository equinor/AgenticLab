using System.Diagnostics.Metrics;
using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Metrics;
using AgenticLab.AiService.Application.Agents;
using AgenticLab.AiService.Application.Flow;
using AgenticLab.AiService.Demo.Vendors;
using Xunit;

namespace AgenticLab.AiService.Tests;

public sealed class MonitoringTests
{
    [Theory]
    [InlineData(null, "default")]
    [InlineData(" ", "default")]
    [InlineData("TEST-HOST", "test-host")]
    [InlineData("anything-a-client-sends", "default")]
    public void HostKeyOnlyReturnsRegisteredKeys(string? vendor, string expected)
    {
        var vendors = new VendorHarnessCatalog([new DefaultHarness(), new TestHarness()]);

        Assert.Equal(expected, vendors.HostKey(vendor));
    }

    [Fact]
    public void ChatMessagesAreCountedByAgentAndHost()
    {
        var agent = $"Agent-{Guid.NewGuid():n}";
        var recorded = new List<KeyValuePair<string, object?>[]>();
        using var listener = new MeterListener();
        listener.InstrumentPublished = (instrument, current) =>
        {
            if (instrument.Meter.Name == ChatMetrics.MeterName)
            {
                current.EnableMeasurementEvents(instrument);
            }
        };
        listener.SetMeasurementEventCallback<long>((_, value, tags, _) =>
        {
            if (tags.ToArray().Any(tag => Equals(tag.Value, agent)))
            {
                Assert.Equal(1, value);
                recorded.Add(tags.ToArray());
            }
        });
        listener.Start();

        ChatMetrics.RecordMessage(agent, "default");

        var tags = Assert.Single(recorded);
        Assert.Equal(["agent", "host"], tags.Select(tag => tag.Key).Order());
        Assert.Contains(new KeyValuePair<string, object?>("host", "default"), tags);
    }

    [Fact]
    public async Task PrometheusEndpointExposesChatMetricsWhenEnabled()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var app = CreateApp(enabled: true);
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        var agent = $"Agent{Guid.NewGuid():n}";

        ChatMetrics.RecordMessage(agent, "default");
        var body = await client.GetStringAsync("/metrics", timeout.Token);

        Assert.Contains($"agenticlab_chat_messages_total{{otel_scope_name=\"{ChatMetrics.MeterName}\",agent=\"{agent}\",host=\"default\"}} 1", body);
    }

    [Fact]
    public async Task PrometheusEndpointIsOffByDefault()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var app = CreateApp(enabled: false);
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };

        using var response = await client.GetAsync("/metrics", timeout.Token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static WebApplication CreateApp(bool enabled)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Configuration[Extensions.PrometheusEndpointKey] = enabled ? "true" : "false";
        builder.AddServiceDefaults();
        builder.Services.AddOpenTelemetry().WithMetrics(metrics => metrics.AddMeter(ChatMetrics.MeterName));
        var app = builder.Build();
        app.MapDefaultEndpoints();
        return app;
    }

    private sealed class TestHarness : IVendorHarness
    {
        public string Key => "test-host";
        public string Harness => "Test harness.";
        public string DisplayName => "Test";
        public string ModelLabel => "Test model";
        public IReadOnlyList<VendorMode> Modes => [];
    }
}
