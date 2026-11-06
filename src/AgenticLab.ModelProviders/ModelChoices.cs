using Microsoft.Extensions.Configuration;

namespace AgenticLab.ModelProviders;

/// <summary>A model or Azure deployment users may select, and the name shown for it.</summary>
/// <param name="Id">The provider model identifier or Azure deployment name sent to the provider.</param>
/// <param name="Name">The display name; never an endpoint or credential.</param>
public sealed record ModelChoice(string Id, string Name);

/// <summary>
/// The server-side allow-list of models a caller may select per request, read from the selected provider's
/// <c>{Provider}:Choices</c>: a comma-separated list such as <c>gpt-5.6-luna=Luna, gpt-4o</c>,
/// where an optional <c>=Name</c> overrides the name derived from the id. Azure's list ships in
/// <see cref="ModelDefaults"/>. The provider's default model (<c>AzureOpenAI:Deployment</c> or
/// <c>{Provider}:Model</c>) is always selectable and preselected; it is added first when not listed.
/// Requests naming any other model are rejected.
/// </summary>
public sealed class ModelChoices
{
    private readonly StringComparer _comparer;

    /// <summary>Reads and validates the choices for the selected provider.</summary>
    /// <exception cref="InvalidOperationException">An entry has no id or an id is repeated.</exception>
    public ModelChoices(IConfiguration configuration, ModelConnectionOptions connection)
    {
        // Azure deployment names are case-insensitive; native model identifiers are not.
        _comparer = connection.Provider == ModelProvider.AzureOpenAI ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var key = KeyFor(connection.Provider);
        var choices = new List<ModelChoice>();
        foreach (var entry in (configuration[key] ?? string.Empty).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = entry.IndexOf('=');
            var id = (separator < 0 ? entry : entry[..separator]).Trim();
            var name = separator < 0 ? string.Empty : entry[(separator + 1)..].Trim();
            if (id.Length == 0)
                throw new InvalidOperationException($"Invalid configuration '{key}': every entry needs a model id.");
            if (choices.Any(choice => _comparer.Equals(choice.Id, id)))
                throw new InvalidOperationException($"Duplicate model '{id}' in configuration '{key}'.");
            choices.Add(new ModelChoice(id, name.Length == 0 ? DisplayName(id) : name));
        }

        Default = choices.FirstOrDefault(choice => _comparer.Equals(choice.Id, connection.DefaultModel))
            ?? new ModelChoice(connection.DefaultModel, DisplayName(connection.DefaultModel));
        if (!choices.Contains(Default)) choices.Insert(0, Default);
        All = choices;
    }

    /// <summary>The configuration key holding the selected provider's choices.</summary>
    public static string KeyFor(ModelProvider provider) => $"{provider}:Choices";

    /// <summary>
    /// A readable name derived from a model id: <c>gpt-5.6-luna</c> becomes <c>GPT-5.6 Luna</c> and
    /// <c>gemini-2.5-flash</c> becomes <c>Gemini 2.5 Flash</c>.
    /// </summary>
    public static string DisplayName(string id)
    {
        var parts = id.Split('-', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count > 1 && parts[0].Equals("gpt", StringComparison.OrdinalIgnoreCase))
        {
            parts[1] = $"GPT-{parts[1]}";
            parts.RemoveAt(0);
        }
        return string.Join(' ', parts.Select(part => char.ToUpperInvariant(part[0]) + part[1..]));
    }

    /// <summary>The selectable models in configured order.</summary>
    public IReadOnlyList<ModelChoice> All { get; }

    /// <summary>The provider's default model, preselected by clients.</summary>
    public ModelChoice Default { get; }

    /// <summary>
    /// Validates a requested model against the allow-list. A null or blank request is valid and means
    /// "no selection", leaving <paramref name="model"/> null; a listed one returns its configured spelling.
    /// </summary>
    /// <param name="requested">The model a caller asked for.</param>
    /// <param name="model">The canonical allow-listed id, or null when nothing was requested.</param>
    /// <returns><c>false</c> only when a model was requested that is not on the allow-list.</returns>
    public bool TryResolve(string? requested, out string? model)
    {
        model = null;
        if (string.IsNullOrWhiteSpace(requested)) return true;
        var trimmed = requested.Trim();
        model = All.FirstOrDefault(choice => _comparer.Equals(choice.Id, trimmed))?.Id;
        return model is not null;
    }
}
