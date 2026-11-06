namespace AgenticLab.AiService.Application.Models;

/// <summary>
/// An ambient, per-run scope carrying the model the caller selected (already validated against
/// <see cref="ModelProviders.ModelChoices"/>) and the run's <see cref="ModelUsage"/>. Provider-backed agents
/// route every model call to <see cref="Model"/> when it is set; A2A delegations forward it as a header.
/// Like the other run scopes it lives in an <see cref="AsyncLocal{T}"/> and is re-activated by <see cref="RunScopeSet"/>.
/// </summary>
public sealed class ModelSelectionScope : IDisposable
{
    private static readonly AsyncLocal<ModelSelectionScope?> CurrentScope = new();

    private ModelSelectionScope(string? model) => Model = model;

    /// <summary>The scope active on the current async context, or null outside an agent run.</summary>
    public static ModelSelectionScope? Current => CurrentScope.Value;

    /// <summary>The selected model, or null to keep each agent's configured model.</summary>
    public string? Model { get; }

    /// <summary>What the run's model calls consumed, accumulated across round-trips.</summary>
    public ModelUsage Usage { get; } = new();

    /// <summary>Starts a scope on the current async context; dispose to end it.</summary>
    /// <param name="model">An allow-listed model, or null for no selection.</param>
    public static ModelSelectionScope Begin(string? model)
    {
        var scope = new ModelSelectionScope(model);
        CurrentScope.Value = scope;
        return scope;
    }

    /// <summary>Re-asserts this scope after an owning async iterator resumed from a <c>yield</c>.</summary>
    public void Activate() => CurrentScope.Value = this;

    /// <summary>Ends the scope, clearing it from the current async context.</summary>
    public void Dispose()
    {
        if (ReferenceEquals(CurrentScope.Value, this))
        {
            CurrentScope.Value = null;
        }
    }
}
