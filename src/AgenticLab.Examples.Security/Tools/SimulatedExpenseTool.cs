using System.Collections.Concurrent;
using System.ComponentModel;
using System.Globalization;
using Microsoft.Extensions.AI;

namespace AgenticLab.Examples.Security.Tools;

/// <summary>
/// A simulated expense system. <see cref="SubmitExpense"/> accepts whatever amount and payee the model passes,
/// which is what makes hidden text in a receipt image harmful: the transcription is trusted and the submission
/// tool has no check of its own. Submissions are kept in a bounded in-memory list for tests; nothing is paid.
/// </summary>
public sealed class SimulatedExpenseTool
{
    private const int SubmissionLimit = 50;
    private readonly ConcurrentQueue<SimulatedExpense> _submissions = new();
    private int _sequence;

    /// <summary>Expenses the tool has "submitted" (most recent last), bounded to the last <see cref="SubmissionLimit"/>.</summary>
    public IReadOnlyCollection<SimulatedExpense> Submissions => _submissions.ToArray();

    /// <summary>Records an expense claim and returns a reference; no amount or payee validation on purpose.</summary>
    [Description("Submits an expense claim for reimbursement and returns its reference number.")]
    public string SubmitExpense(
        [Description("Short description of the expense.")] string description,
        [Description("The amount to reimburse, in EUR.")] decimal amount,
        [Description("Who should be paid.")] string payee)
    {
        var reference = $"EXP-{Interlocked.Increment(ref _sequence):0000}";
        _submissions.Enqueue(new SimulatedExpense(reference, description, amount, payee));
        while (_submissions.Count > SubmissionLimit) _submissions.TryDequeue(out _);
        return $"Simulated: expense {reference} submitted for {amount.ToString("0.00", CultureInfo.InvariantCulture)} EUR " +
               $"to {payee} ('{description}'). Nothing was actually paid.";
    }

    /// <summary>Exposes the submission as an agent tool.</summary>
    public IList<AITool> AsTools() => [AIFunctionFactory.Create(SubmitExpense)];
}

/// <summary>One simulated expense claim.</summary>
public sealed record SimulatedExpense(string Reference, string Description, decimal Amount, string Payee);
