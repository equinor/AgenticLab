using Microsoft.AspNetCore.Components;
using AgenticLab.Web.Learning;

namespace AgenticLab.Web.Components.Pages;

/// <summary>
/// Plays one learning path: a step rail with browser-local progress and the current step's explain, predict,
/// try and reflect parts. Opening a step marks it visited; only Finish step completes it. Needs no AI service.
/// </summary>
public partial class PathPlayer
{
    [Inject]
    private LearningProgressStore Store { get; set; } = default!;

    [Inject]
    private NavigationManager Navigation { get; set; } = default!;

    /// <summary>The path identifier from the route.</summary>
    [Parameter]
    public string? PathId { get; set; }

    /// <summary>The optional stable step identifier; without it the path resumes or opens its first step.</summary>
    [SupplyParameterFromQuery(Name = "step")]
    public string? StepId { get; set; }

    private LearningPath? _path;
    private PathStep? _step;
    private LearningProgress _progress = new();
    private bool _loaded;
    private bool _focusHeading;
    private ElementReference _heading;

    private bool OnPathRoute => Navigation.ToBaseRelativePath(Navigation.Uri).Split('?', '#')[0]
        .StartsWith("learn/paths/", StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // Leaving the page changes the query string before this component is removed; that is not a step change.
        if (_loaded && !OnPathRoute)
        {
            return;
        }

        var previous = _step;
        _path = LearningPaths.Find(PathId);
        _step = _path is { Available: true } path ? path.Find(StepId) ?? _progress.Resume(path) ?? path.Steps[0] : null;
        _focusHeading = _loaded && previous is not null && _step is not null && previous.Id != _step.Id;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _progress = await Store.LoadAsync();
            _loaded = true;
            if (_path is { Available: true } path && path.Find(StepId) is null)
            {
                _step = _progress.Resume(path) ?? _step;
            }

            StateHasChanged();
            return;
        }

        if (!_loaded)
        {
            return;
        }

        if (_path is { } current && _step is { } step && _progress.Visit(current, step))
        {
            await Store.SaveAsync(_progress);
            StateHasChanged();
        }

        if (_focusHeading)
        {
            _focusHeading = false;
            try
            {
                await _heading.FocusAsync();
            }
            catch (Microsoft.JSInterop.JSException)
            {
                // The heading was removed by a navigation that started after this render.
            }
        }
    }

    private Task FinishAsync() => RecordAsync(complete: true);

    private Task SkipAsync() => RecordAsync(complete: false);

    private async Task RecordAsync(bool complete)
    {
        if (!_loaded || _path is not { } path || _step is not { } step)
        {
            return;
        }

        if (complete)
        {
            _progress.Complete(path, step);
        }
        else
        {
            _progress.Skip(path, step);
        }

        await Store.SaveAsync(_progress);
        if (path.Neighbour(step, 1) is { } next)
        {
            Navigation.NavigateTo(LearningPaths.StepHref(path, next));
        }
    }

    private async Task ResetAsync()
    {
        if (!_loaded || _path is not { Available: true } path)
        {
            return;
        }

        _progress.Reset(path);
        await Store.SaveAsync(_progress);
        // Show the first step now, so the step being left is not recorded as visited again before navigating.
        _step = path.Steps[0];
        Navigation.NavigateTo(LearningPaths.StepHref(path, _step));
    }
}
