using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace AgenticLab.Web.Learning;

/// <summary>
/// What the learner did with a step. Opening a step only makes it <see cref="Visited"/>; completing and
/// skipping are explicit choices, and a skipped step is not completed.
/// </summary>
internal enum StepStatus
{
    NotVisited,
    Visited,
    Completed,
    Skipped,
}

/// <summary>
/// Learning-path progress kept in the browser: a status per step and the step to resume per path. It holds
/// identifiers only, never answers, predictions or run content. Stored data that is malformed, from another
/// version, or names paths and steps that no longer exist is dropped instead of failing.
/// </summary>
internal sealed class LearningProgress
{
    /// <summary>The <c>localStorage</c> key.</summary>
    public const string StorageKey = "agenticlab-learning-progress";

    private const int Version = 1;
    private const int MaxStoredLength = 16_384;

    private readonly Dictionary<string, Dictionary<string, StepStatus>> _steps = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _resume = new(StringComparer.Ordinal);

    /// <summary>The recorded status of a step.</summary>
    public StepStatus Status(LearningPath path, PathStep step) =>
        _steps.TryGetValue(path.Id, out var steps) && steps.TryGetValue(step.Id, out var status) ? status : StepStatus.NotVisited;

    /// <summary>The step last opened on the path, or null when it was never opened.</summary>
    public PathStep? Resume(LearningPath path) =>
        _resume.TryGetValue(path.Id, out var stepId) ? path.Find(stepId) : null;

    /// <summary>How many of the path's steps are completed.</summary>
    public int CompletedCount(LearningPath path) =>
        path.Steps.Count(step => Status(path, step) == StepStatus.Completed);

    /// <summary>Whether anything is recorded for the path.</summary>
    public bool HasProgress(LearningPath path) => _steps.ContainsKey(path.Id) || _resume.ContainsKey(path.Id);

    /// <summary>
    /// Records that a step was opened and makes it the resume location. An existing completed or skipped
    /// status is kept. Returns whether anything changed.
    /// </summary>
    public bool Visit(LearningPath path, PathStep step)
    {
        var resumed = !_resume.TryGetValue(path.Id, out var current) || current != step.Id;
        _resume[path.Id] = step.Id;
        if (Status(path, step) != StepStatus.NotVisited)
        {
            return resumed;
        }

        Set(path, step, StepStatus.Visited);
        return true;
    }

    /// <summary>Marks a step completed by the learner's explicit choice.</summary>
    public void Complete(LearningPath path, PathStep step) => Set(path, step, StepStatus.Completed);

    /// <summary>Marks a step skipped; it does not count as completed.</summary>
    public void Skip(LearningPath path, PathStep step) => Set(path, step, StepStatus.Skipped);

    /// <summary>Forgets everything recorded for the path.</summary>
    public void Reset(LearningPath path)
    {
        _steps.Remove(path.Id);
        _resume.Remove(path.Id);
    }

    private void Set(LearningPath path, PathStep step, StepStatus status)
    {
        if (!_steps.TryGetValue(path.Id, out var steps))
        {
            _steps[path.Id] = steps = new(StringComparer.Ordinal);
        }

        steps[step.Id] = status;
    }

    /// <summary>Serialises the progress as versioned JSON.</summary>
    public string Serialize()
    {
        var paths = _steps.Keys.Union(_resume.Keys).ToDictionary(
            pathId => pathId,
            StoredPath? (pathId) => new StoredPath(
                _resume.GetValueOrDefault(pathId),
                _steps.TryGetValue(pathId, out var steps)
                    ? steps.ToDictionary(entry => entry.Key, entry => entry.Value.ToString().ToLowerInvariant())
                    : []));
        return JsonSerializer.Serialize(new StoredProgress(Version, paths));
    }

    /// <summary>Reads stored progress, keeping only entries that match the current catalogue.</summary>
    public static LearningProgress Parse(string? json)
    {
        var progress = new LearningProgress();
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxStoredLength)
        {
            return progress;
        }

        StoredProgress? stored;
        try
        {
            stored = JsonSerializer.Deserialize<StoredProgress>(json);
        }
        catch (JsonException)
        {
            return progress;
        }

        if (stored is not { Version: Version, Paths: not null })
        {
            return progress;
        }

        foreach (var (pathId, entry) in stored.Paths)
        {
            if (LearningPaths.Find(pathId) is not { } path || entry is null)
            {
                continue;
            }

            foreach (var (stepId, value) in entry.Steps ?? [])
            {
                if (path.Find(stepId) is { } step && Enum.TryParse<StepStatus>(value, ignoreCase: true, out var status)
                    && status is StepStatus.Visited or StepStatus.Completed or StepStatus.Skipped)
                {
                    progress.Set(path, step, status);
                }
            }

            if (path.Find(entry.Resume) is { } resume)
            {
                progress._resume[path.Id] = resume.Id;
            }
        }

        return progress;
    }

    private sealed record StoredProgress(
        [property: JsonPropertyName("v")] int Version,
        [property: JsonPropertyName("paths")] Dictionary<string, StoredPath?>? Paths);

    private sealed record StoredPath(
        [property: JsonPropertyName("resume")] string? Resume,
        [property: JsonPropertyName("steps")] Dictionary<string, string>? Steps);
}

/// <summary>
/// Reads and writes <see cref="LearningProgress"/> in the browser's <c>localStorage</c>. Storage is best-effort:
/// when it is unavailable the paths still work, without remembering progress.
/// </summary>
internal sealed class LearningProgressStore(IJSRuntime js)
{
    /// <summary>Loads stored progress, or empty progress when storage cannot be read.</summary>
    public async Task<LearningProgress> LoadAsync()
    {
        try
        {
            return LearningProgress.Parse(await js.InvokeAsync<string?>("localStorage.getItem", LearningProgress.StorageKey));
        }
        catch
        {
            return new LearningProgress();
        }
    }

    /// <summary>Saves progress; a failure is ignored.</summary>
    public async Task SaveAsync(LearningProgress progress)
    {
        try
        {
            await js.InvokeVoidAsync("localStorage.setItem", LearningProgress.StorageKey, progress.Serialize());
        }
        catch
        {
        }
    }
}
