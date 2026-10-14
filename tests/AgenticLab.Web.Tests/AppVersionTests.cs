using System.Net;
using System.Reflection;
using AgenticLab.ServiceDefaults;
using AgenticLab.Web.Components.Shared;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace AgenticLab.Web.Tests;

public sealed class AppVersionTests
{
    private const string VersionHeader = "X-AgenticLab-Version";
    private static string ExpectedVersion => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "version.txt")).Trim();

    [Fact]
    public void SharedSingletonAndAssembliesUseTheCentralReleaseVersion()
    {
        using var services = CreateBuilder().Services.BuildServiceProvider();
        var version = services.GetRequiredService<ApplicationVersionInfo>();
        Assert.False(string.IsNullOrWhiteSpace(ExpectedVersion));
        Assert.Equal(ExpectedVersion, version.Version);
        Assert.Same(version, services.GetRequiredService<ApplicationVersionInfo>());

        foreach (var assembly in new[] { typeof(ApplicationVersionInfo).Assembly, typeof(AppHeader).Assembly, typeof(AppVersionTests).Assembly })
        {
            var metadata = Assert.Single(assembly.GetCustomAttributes<AssemblyMetadataAttribute>(),
                attribute => attribute.Key == "ApplicationVersion");
            Assert.Equal(ExpectedVersion, metadata.Value);
        }
    }

    [Fact]
    public async Task HeaderRendersTheVersionRatherThanTheRazorExpression()
    {
        await using var services = CreateBuilder().Services.BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var rendered = await renderer.RenderComponentAsync<AppHeader>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(AppHeader.Context)] = "Home" }));
            return rendered.ToHtmlString();
        });

        Assert.Contains($">v{ExpectedVersion}</span>", html);
        Assert.Contains($"title=\"Application version {ExpectedVersion}\"", html);
        Assert.Contains("aria-label=\"Agentic Lab home\"", html);
        Assert.DoesNotContain("v@AppVersion.Version", html);
    }

    [Theory]
    [InlineData("GET", "/ok", HttpStatusCode.OK, "ready")]
    [InlineData("HEAD", "/ok", HttpStatusCode.OK, "")]
    [InlineData("GET", "/empty", HttpStatusCode.NoContent, "")]
    [InlineData("GET", "/missing", HttpStatusCode.NotFound, "")]
    [InlineData("GET", "/throw", HttpStatusCode.InternalServerError, "error")]
    public async Task MiddlewareAddsOneHeaderWithoutChangingStatusOrBody(string method, string path, HttpStatusCode status, string body)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var app = CreateBuilder().Build();
        app.UseAppVersion();
        app.UseExceptionHandler("/error");
        app.MapMethods("/ok", ["GET", "HEAD"], () => "ready");
        app.MapGet("/empty", () => TypedResults.NoContent());
        app.MapGet("/throw", (Func<string>)(() => throw new InvalidOperationException("Synthetic failure")));
        app.MapGet("/error", () => TypedResults.Text("error", statusCode: 500));
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await client.SendAsync(request, timeout.Token);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(body, await response.Content.ReadAsStringAsync(timeout.Token));
        Assert.Equal(ExpectedVersion, Assert.Single(response.Headers.GetValues(VersionHeader)));
        Assert.False(response.Headers.Contains("X-Ent-Lab-Version"));
        Assert.False(response.Headers.Contains("X-App-Version"));
    }

    [Fact]
    public async Task StaticFilesIncludeTheSameVersionHeader()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var files = new PhysicalFileProvider(AppContext.BaseDirectory);
        await using var app = CreateBuilder().Build();
        app.UseAppVersion();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files, RequestPath = "/static" });
        await app.StartAsync(timeout.Token);
        using var client = new HttpClient { BaseAddress = new Uri(Assert.Single(app.Urls)) };
        using var response = await client.GetAsync("/static/version.txt", timeout.Token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(ExpectedVersion, (await response.Content.ReadAsStringAsync(timeout.Token)).Trim());
        Assert.Equal(ExpectedVersion, Assert.Single(response.Headers.GetValues(VersionHeader)));
    }

    private static WebApplicationBuilder CreateBuilder()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.AddServiceDefaults();
        builder.Logging.ClearProviders();
        return builder;
    }
}