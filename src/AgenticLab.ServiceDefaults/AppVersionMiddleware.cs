using Microsoft.AspNetCore.Http;

namespace AgenticLab.ServiceDefaults;

/// <summary>Adds release traceability without buffering responses or changing when streams start.</summary>
public sealed class AppVersionMiddleware(RequestDelegate next, ApplicationVersionInfo version)
{
    /// <summary>The response header shared by all application HTTP hosts.</summary>
    public const string HeaderName = "X-AgenticLab-Version";

    /// <summary>Publishes the release before headers are sent, preserving an upstream proxy response's version.</summary>
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            context.Response.Headers.TryAdd(HeaderName, version.Version);
            return Task.CompletedTask;
        });
        return next(context);
    }
}