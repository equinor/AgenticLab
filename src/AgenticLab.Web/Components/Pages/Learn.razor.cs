using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using AgenticLab.Web.Components.Pages.LearningParts;
using AgenticLab.Web.Learning;

namespace AgenticLab.Web.Components.Pages;

/// <summary>A standalone, URL-addressable guide using the existing concept catalog without an AI service dependency.</summary>
public partial class Learn : IAsyncDisposable
{
    [Inject]
    private ConceptCatalog Concepts { get; set; } = default!;

    [Inject] private NavigationManager Navigation { get; set; } = default!;
    [Inject] private IJSRuntime JS { get; set; } = default!;

    /// <summary>The optional stable stage identifier supplied by the query string.</summary>
    [SupplyParameterFromQuery(Name = "stage")]
    public string? StageId { get; set; }

    private static IReadOnlyList<LearningStage> Stages => AgentLearningJourney.Stages;
    private LearningStage _stage = Stages[0];
    private int _stageIndex;
    private int _introStep;
    private bool _initialized;
    private Concept? _activeConcept;
    private ElementReference _stageHeading;
    private ElementReference _conceptHeading;
    private ElementReference _relatedHeading;
    private FocusTarget _pendingFocus;
    private ElementReference _root;
    private FoundationLesson? _foundationLesson;
    private AgentTurnLesson? _turnLesson;
    private ToolCallingLesson? _toolLesson;
    private HostingLesson? _hostingLesson;
    private IJSObjectReference? _keyboardModule;
    private DotNetObjectReference<Learn>? _keyboardReference;
    private string? _navigatingTo;
    private string? _lastStepStage;
    private TaskCompletionSource? _keyboardUpdate;
    private bool _checkKeyboardFocus;
    private bool _disposed;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var next = AgentLearningJourney.Resolve(StageId);
        if (_navigatingTo != next.Id)
        {
            _navigatingTo = null;
            _lastStepStage = null;
        }
        if (_initialized && next.Id != _stage.Id)
        {
            _pendingFocus = FocusTarget.Stage;
            _introStep = 0;
        }

        _activeConcept = null;
        _stage = next;
        _stageIndex = AgentLearningJourney.IndexOf(next.Id);
        _initialized = true;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var keyboardUpdate = _keyboardUpdate;
        if (firstRender)
        {
            var module = await JS.InvokeAsync<IJSObjectReference>("import", "./Components/Pages/Learn.razor.js");
            if (_disposed)
            {
                await module.DisposeAsync();
                return;
            }
            _keyboardModule = module;
            _keyboardReference = DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("attach", _root, _keyboardReference);
        }

        if (_lastStepStage == _stage.Id)
        {
            _lastStepStage = null;
            switch (_stage.Id)
            {
                case "why-agents":
                    _introStep = AgentLearningJourney.IntroductionSteps.Count - 1;
                    StateHasChanged();
                    break;
                case "agent-loop": _turnLesson?.MoveToLastStep(); break;
                case "tool-calling": _toolLesson?.MoveToLastStep(); break;
                case "where-to-run": _hostingLesson?.MoveToLastStep(); break;
                case "agent-landscape" or "model-to-agent" or "inside-the-harness" or "agents-everywhere" or "anatomy-of-agent":
                    _foundationLesson?.MoveToLastStep();
                    break;
            }
        }

        var target = _pendingFocus;
        _pendingFocus = FocusTarget.None;
        if (target != FocusTarget.None)
        {
            var element = target switch
            {
                FocusTarget.Concept => _conceptHeading,
                FocusTarget.Related => _relatedHeading,
                _ => _stageHeading,
            };
            await element.FocusAsync();
        }
        if (_checkKeyboardFocus && _keyboardModule is not null)
        {
            _checkKeyboardFocus = false;
            await _keyboardModule.InvokeVoidAsync("restoreFocus", _root, _stageHeading);
        }
        if (_navigatingTo == _stage.Id) _navigatingTo = null;
        if (_keyboardUpdate == keyboardUpdate)
        {
            _keyboardUpdate = null;
            keyboardUpdate?.TrySetResult();
        }
    }

    /// <summary>Advances a reveal or crosses a lesson boundary using the existing stage URL.</summary>
    [JSInvokable]
    public async Task NavigateByKeyboardAsync(int direction)
    {
        Task? update = null;
        await InvokeAsync(() =>
        {
            if (_disposed || _navigatingTo is not null || direction is not (-1 or 1)) return;
            var moved = _stage.Id switch
            {
                "why-agents" => TryMoveIntroduction(direction),
                "agent-loop" => _turnLesson?.TryMoveStep(direction) == true,
                "tool-calling" => _toolLesson?.TryMoveStep(direction) == true,
                "where-to-run" => _hostingLesson?.TryMoveStep(direction) == true,
                "agent-landscape" or "model-to-agent" or "inside-the-harness" or "agents-everywhere" or "anatomy-of-agent" =>
                    _foundationLesson?.TryMoveStep(direction) == true,
                _ => false,
            };
            if (moved)
            {
                _keyboardUpdate = new(TaskCreationOptions.RunContinuationsAsynchronously);
                update = _keyboardUpdate.Task;
                _checkKeyboardFocus = true;
                StateHasChanged();
                return;
            }

            var next = AgentLearningJourney.Move(_stage.Id, direction);
            if (next.Id == _stage.Id) return;
            _keyboardUpdate = new(TaskCreationOptions.RunContinuationsAsynchronously);
            update = _keyboardUpdate.Task;
            _navigatingTo = next.Id;
            _lastStepStage = direction == -1 ? next.Id : null;
            Navigation.NavigateTo(AgentLearningJourney.Href(next.Id));
        });
        if (update is not null) await update;
    }

    private bool TryMoveIntroduction(int direction)
    {
        var previous = _introStep;
        MoveIntroduction(direction);
        return _introStep != previous;
    }

    private void MoveIntroduction(int offset)
    {
        var next = Math.Clamp(_introStep + offset, 0, AgentLearningJourney.IntroductionSteps.Count - 1);
        if (next == _introStep) return;
        _introStep = next;
    }

    private void OpenConcept(Concept concept)
    {
        _activeConcept = concept;
        _pendingFocus = FocusTarget.Concept;
    }

    private void CloseConcept()
    {
        _activeConcept = null;
        _pendingFocus = FocusTarget.Related;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _keyboardUpdate?.TrySetResult();
        if (_keyboardModule is not null)
        {
            try
            {
                await _keyboardModule.InvokeVoidAsync("detach", _root);
                await _keyboardModule.DisposeAsync();
            }
            catch (JSDisconnectedException) { }
        }
        _keyboardReference?.Dispose();
    }

    private enum FocusTarget { None, Stage, Concept, Related }
}