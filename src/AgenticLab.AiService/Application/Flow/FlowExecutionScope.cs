using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.AI;

namespace AgenticLab.AiService.Application.Flow;

/// <summary>Connects model and tool execution boundaries to the current streaming run.</summary>
public sealed class FlowExecutionScope : IDisposable
{
    private static readonly AsyncLocal<FlowExecutionScope?> Ambient = new();
    private readonly FlowExecutionScope? _previous = Ambient.Value;
    private readonly FlowSession _session;
    private readonly ConcurrentQueue<FunctionResultContent> _completedTools = new();

    /// <summary>Opens a scope for one run, without modifying the shared chat client.</summary>
    public FlowExecutionScope(FlowSession session)
    {
        _session = session;
        Activate();
    }

    /// <summary>The scope active in this asynchronous execution context.</summary>
    public static FlowExecutionScope? Current => Ambient.Value;

    /// <summary>Restores this scope when a streaming iterator resumes.</summary>
    public void Activate() => Ambient.Value = this;

    /// <summary>Waits at an enabled execution boundary.</summary>
    public Task WaitAsync(string kind, string? tool, CancellationToken cancellationToken) =>
        _session.WaitForBreakpointAsync(kind, tool, cancellationToken);

    /// <summary>
    /// Takes a tool result recorded as soon as its function returned. The SDK only streams results after
    /// every call in a model response has run, so this lets a paused run show results that already exist.
    /// </summary>
    public bool TryTakeCompletedTool([NotNullWhen(true)] out FunctionResultContent? result) =>
        _completedTools.TryDequeue(out result);

    /// <summary>Whether the most recent advance produced an agent update.</summary>
    public bool HasUpdate { get; private set; }

    /// <summary>Advances once, delivering breakpoint notices even while execution is suspended inside that advance.</summary>
    public async IAsyncEnumerable<BreakpointNotice> AdvanceAsync<T>(
        IAsyncEnumerator<T> updates,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var notificationWait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var advance = updates.MoveNextAsync().AsTask();
        try
        {
            while (true)
            {
                while (_session.BreakpointEvents.Reader.TryRead(out var notice))
                {
                    yield return notice;
                }

                if (advance.IsCompleted)
                {
                    HasUpdate = await advance;
                    while (_session.BreakpointEvents.Reader.TryRead(out var completedNotice))
                    {
                        yield return completedNotice;
                    }
                    break;
                }

                var available = _session.BreakpointEvents.Reader.WaitToReadAsync(notificationWait.Token).AsTask();
                await Task.WhenAny(advance, available);
                cancellationToken.ThrowIfCancellationRequested();
            }
        }
        finally
        {
            await notificationWait.CancelAsync();
            if (!advance.IsCompleted)
            {
                _session.Stop();
                try { await advance; }
                catch (OperationCanceledException) { }
            }
        }
    }

    /// <summary>Invokes one SDK function, preserving its arguments and result across the execution gates.</summary>
    public static async ValueTask<object?> InvokeFunctionAsync(FunctionInvocationContext context, CancellationToken cancellationToken)
    {
        var scope = Current;
        if (scope is not null)
        {
            await scope.WaitAsync("before-tool", context.Function.Name, cancellationToken);
        }

        var result = await context.Function.InvokeAsync(context.Arguments, cancellationToken);
        if (scope is not null)
        {
            scope._completedTools.Enqueue(new FunctionResultContent(context.CallContent.CallId, result));
            await scope.WaitAsync("after-tool", context.Function.Name, cancellationToken);
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose() => Ambient.Value = _previous;
}