using System.Collections.Concurrent;
using AgenticLab.ModelProviders;
using Microsoft.Extensions.AI;

namespace AgenticLab.A2AServer;

/// <summary>
/// Lets a caller choose the model per A2A request with the <see cref="ModelSelectionHeader"/>, validated
/// against this host's own <see cref="ModelChoices"/>. Hosted agents share one chat client that routes to
/// the request's model, or to the provider default when the header is absent.
/// </summary>
public static class SelectableModels
{
    private static readonly AsyncLocal<string?> Selected = new();

    /// <summary>The model selected for the current request, or null.</summary>
    public static string? Current => Selected.Value;

    /// <summary>Builds the shared agent client on the configured provider.</summary>
    public static IChatClient CreateChatClient(ModelConnectionOptions connection)
    {
        var factory = new ModelClientFactory(connection);
        return CreateChatClient(connection.DefaultModel, model => factory.Create(model));
    }

    /// <summary>Builds the shared agent client from a raw client per model, cached and instrumented per model.</summary>
    /// <param name="defaultModel">The model used when a request selects none.</param>
    /// <param name="create">Creates the raw client for a model.</param>
    public static IChatClient CreateChatClient(string defaultModel, Func<string, IChatClient> create)
    {
        var models = new ConcurrentDictionary<string, IChatClient>(StringComparer.Ordinal);
        return new ModelRoutingChatClient(defaultModel, () => Current,
                model => models.GetOrAdd(model, id => create(id).AsBuilder().UseOpenTelemetry().Build()))
            .AsBuilder()
            .UseFunctionInvocation()
            .Build();
    }

    /// <summary>
    /// Applies the request's selected model for the rest of the pipeline, rejecting a model that is not on
    /// <paramref name="choices"/> with 400 before any agent runs.
    /// </summary>
    public static IApplicationBuilder UseModelSelection(this IApplicationBuilder app, ModelChoices choices) =>
        app.Use(async (context, next) =>
        {
            var requested = context.Request.Headers[ModelSelectionHeader.Name].ToString();
            if (!choices.TryResolve(requested, out var model))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsync($"Unknown model '{requested}'.");
                return;
            }

            Selected.Value = model;
            try
            {
                await next(context);
            }
            finally
            {
                Selected.Value = null;
            }
        });
}
