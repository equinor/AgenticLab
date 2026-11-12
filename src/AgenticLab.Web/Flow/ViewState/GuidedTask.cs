using AgenticLab.Web.Learning;

namespace AgenticLab.Web.Flow;

/// <summary>
/// The learning-path exercise shown in the Learn dock: which catalogue step it is, why its live setup could
/// not be prepared (if so), and whether the dock shows the task or the concept reference. It holds no run
/// state and never completes a step; progress stays with the path pages.
/// </summary>
internal sealed class GuidedTask(Func<bool> conceptOpen, Action notify)
{
    private bool _taskVisible;

    /// <summary>The path the exercise belongs to, or null when no exercise was opened.</summary>
    public LearningPath? Path { get; private set; }

    /// <summary>The exercise step, or null when no exercise was opened.</summary>
    public PathStep? Step { get; private set; }

    /// <summary>Why the live exercise was not prepared; null when its agent and tools were available.</summary>
    public string? Unavailable { get; private set; }

    /// <summary>Whether the page was opened for a path exercise.</summary>
    public bool Active => Step is not null;

    /// <summary>Whether the dock shows the task. An open concept takes its place until it is closed.</summary>
    public bool ShowingTask => Active && _taskVisible && !conceptOpen();

    /// <summary>Opens the task for a catalogue step, with the reason its setup was skipped, if any.</summary>
    public void Start(LearningPath path, PathStep step, string? unavailable)
    {
        Path = path;
        Step = step;
        Unavailable = unavailable;
        _taskVisible = true;
        notify();
    }

    /// <summary>Returns the dock to the task after browsing concepts.</summary>
    public void ShowTask()
    {
        _taskVisible = true;
        notify();
    }

    /// <summary>Shows the concept index in place of the task, which keeps its position.</summary>
    public void BrowseConcepts()
    {
        _taskVisible = false;
        notify();
    }

    /// <summary>
    /// Checks that the exercise's agent is offered by its host and has the tools the exercise depends on.
    /// Returns the reason it cannot be prepared, or null. A listed agent does not prove the model is reachable.
    /// </summary>
    public static string? Check(PathExercise exercise, AgentRoster roster)
    {
        var name = exercise.Setup.Agent;
        var host = exercise.Setup.Host is { } key
            ? roster.AvailableHosts.FirstOrDefault(candidate => string.Equals(candidate.Key, key, StringComparison.OrdinalIgnoreCase))
            : roster.CurrentVendorInfo;
        var agent = host?.Modes.Any(mode => string.Equals(mode.Agent, name, StringComparison.OrdinalIgnoreCase)) == true
            ? roster.Agents.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.OrdinalIgnoreCase))
            : null;
        if (agent is null)
        {
            return $"The {name} agent is not available here.";
        }

        var missing = exercise.RequiredTools
            .Where(tool => !agent.Tools.Contains(tool, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        return missing.Length == 0 ? null : $"The {name} agent does not have {string.Join(" and ", missing)} here.";
    }
}
