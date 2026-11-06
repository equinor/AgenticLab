namespace AgenticLab.Web.Flow;

/// <summary>
/// The models a run may select, fetched once from <c>GET /models</c>, and the user's choice. The choice
/// applies to every following message until changed, is restored from the browser and is sent with each
/// run; the service rejects anything else. Without an answer (an older service or a failed call) nothing
/// is selectable and runs keep each agent's own model.
/// </summary>
internal sealed class ModelSelection(Action notify)
{
    private IReadOnlyList<ModelChoiceInfo> _choices = [];
    private string? _selected;
    private string? _preferred;

    /// <summary>The selectable models in the service's order.</summary>
    public IReadOnlyList<ModelChoiceInfo> Choices => _choices;

    /// <summary>Whether there is more than one model to choose from.</summary>
    public bool CanChoose => _choices.Count > 1;

    /// <summary>The model sent with the next run; null when the service lists none.</summary>
    public string? Selected
    {
        get => _selected;
        set
        {
            if (value == _selected || !Offers(value)) return;
            _selected = value;
            notify();
        }
    }

    /// <summary>The display name for a model id, falling back to the id itself.</summary>
    public string NameFor(string? id) =>
        _choices.FirstOrDefault(choice => choice.Id == id)?.Name ?? id ?? string.Empty;

    /// <summary>Applies the service's list, keeping a restored choice it still offers, else its default; null changes nothing.</summary>
    public void Set(ModelsResponse? response)
    {
        if (response is null) return;
        _choices = response.Models;
        _selected = Offers(_preferred) ? _preferred
            : Offers(response.Default) ? response.Default
            : _choices.FirstOrDefault()?.Id;
        notify();
    }

    /// <summary>
    /// Restores a stored choice. Before the list has loaded it is remembered and applied by <see cref="Set"/>;
    /// a choice the service no longer offers is ignored.
    /// </summary>
    /// <returns>Whether the selection changed now.</returns>
    public bool Restore(string? stored)
    {
        _preferred = stored;
        var previous = _selected;
        Selected = stored;
        return previous != _selected;
    }

    private bool Offers(string? id) => id is not null && _choices.Any(choice => choice.Id == id);
}
