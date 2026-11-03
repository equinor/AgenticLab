using Microsoft.Extensions.Configuration;

namespace AgenticLab.ModelProviders;

/// <summary>A model or Azure deployment users may select, and the name shown for it.</summary>
/// <param name="Id">The provider model identifier or Azure deployment name sent to the provider.</param>
/// <param name="Name">The display name; never an endpoint or credential.</param>
public sealed record ModelChoice(string Id, string Name);

/// <summary>
/// The server-side allow-list of models a caller may select per request, read from
/// <c>Models:Choices:{i}:Id</c> and optional <c>Models:Choices:{i}:Name</c>. The provider's default model
/// (<c>AzureOpenAI:Deployment</c> or <c>{Provider}:Model</c>) is the default choice and must be listed;
/// without configured choices the allow-list holds only that default. Requests naming any other model are rejected.
/// </summary>
public sealed class ModelChoices
{
    /// <summary>The configuration section holding the ordered choices.</summary>
    public const string SectionName = "Models:Choices";

    private readonly StringComparer _comparer;

    /// <summary>Reads and validates the choices for the selected provider.</summary>
    /// <exception cref="InvalidOperationException">A choice has no id, an id is repeated, or the default model is not listed.</exception>
    public ModelChoices(IConfiguration configuration, ModelConnectionOptions connection)
    {
        // Azure deployment names are case-insensitive; native model identifiers are not.
        _comparer = connection.Provider == ModelProvider.AzureOpenAI ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var choices = new List<ModelChoice>();
        foreach (var entry in configuration.GetSection(SectionName).GetChildren())
        {
            var id = entry["Id"]?.Trim();
            if (string.IsNullOrEmpty(id))
                throw new InvalidOperationException($"Missing configuration '{SectionName}:{entry.Key}:Id'.");
            if (choices.Any(choice => _comparer.Equals(choice.Id, id)))
                throw new InvalidOperationException($"Duplicate model '{id}' in configuration '{SectionName}'.");
            var name = entry["Name"]?.Trim();
            choices.Add(new ModelChoice(id, string.IsNullOrEmpty(name) ? id : name));
        }

        if (choices.Count == 0)
        {
            choices.Add(new ModelChoice(connection.DefaultModel, connection.DefaultModel));
        }

        Default = choices.FirstOrDefault(choice => _comparer.Equals(choice.Id, connection.DefaultModel))
            ?? throw new InvalidOperationException(
                $"Configuration '{SectionName}' must include the provider's default model '{connection.DefaultModel}'.");
        All = choices;
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
