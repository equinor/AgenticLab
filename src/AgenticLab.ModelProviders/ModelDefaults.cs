using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;

namespace AgenticLab.ModelProviders;

/// <summary>
/// Adds the committed Azure OpenAI model defaults from the embedded <c>models.json</c>: the default
/// deployment and the selectable models. They are not secrets, so AiService and A2AServer share one file
/// instead of per-environment variables.
/// </summary>
public static class ModelDefaults
{
    /// <summary>
    /// Inserts the defaults as the lowest-precedence source, so appsettings, user-secrets, environment
    /// variables and command-line values still override them.
    /// </summary>
    public static IConfigurationBuilder AddModelDefaults(this IConfigurationBuilder builder)
    {
        builder.Sources.Insert(0, new MemoryConfigurationSource { InitialData = Load() });
        return builder;
    }

    // Read once into memory: a stream source cannot be reloaded when later sources change.
    private static IEnumerable<KeyValuePair<string, string?>> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("AgenticLab.ModelProviders.models.json")
            ?? throw new InvalidOperationException("The embedded models.json is missing.");
        return new ConfigurationBuilder().AddJsonStream(stream).Build().AsEnumerable()
            .Where(setting => setting.Value is not null).ToList();
    }
}
