using System.Reflection;

namespace AgenticLab.ServiceDefaults;

/// <summary>The shared release identity embedded at build time, independent of runtime configuration and Git metadata.</summary>
public sealed class ApplicationVersionInfo
{
    /// <summary>The application release version used by HTTP responses and frontend metadata.</summary>
    public string Version { get; } = typeof(ApplicationVersionInfo).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .Single(attribute => attribute.Key == "ApplicationVersion").Value
        ?? throw new InvalidOperationException("The application version is missing from the build metadata.");
}