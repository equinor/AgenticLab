using AgenticLab.Examples.Security.Agents;
using AgenticLab.Examples.Security.Tools;

namespace AgenticLab.Examples.Security.Scenarios;

/// <summary>
/// A scripted attack scenario: the agent that plays it, the prompt that triggers it, what the viewer will see
/// happen in the live flow and which harness controls stop it. Everything is simulated in memory.
/// <paramref name="ImagePath"/> is set when the attack arrives through a viewable image the panel should show.
/// </summary>
public sealed record SecurityScenario(
    string Id,
    string AgentName,
    string Title,
    string Attack,
    string Prompt,
    IReadOnlyList<string> WhatHappens,
    IReadOnlyList<string> Mitigations,
    string? ImagePath = null);

/// <summary>
/// The catalogue of scripted scenarios shown in the Security Lab panel and the Learn lesson. One entry per
/// scenario agent; each agent's suggested prompt is the scenario prompt.
/// </summary>
public static class SecurityScenarios
{
    // BACKLOG — further scenarios to add here, each as a new scripted agent + playbook + catalogue entry:
    // - Over-privileged tool / confused deputy: a benign request where the agent holds a delete or payment
    //   tool it never needed, and a stray instruction uses it. Teaches least privilege and per-run toggles.
    // - Rogue A2A specialist: a remote agent's reply carries instructions the orchestrator follows. Teaches
    //   treating peer-agent output as untrusted input (reuse A2AAgentProvider.GetTools(names) bounding).
    // - Poisoned workspace SKILL.md / instructions file: a skill body that redirects the Coder agent. Teaches
    //   reviewing workspace content as code and the Workspace:Mode policy.
    // - Direct jailbreak: the user themselves asks the agent to ignore its guardrails. Teaches that the
    //   persona prompt is not a security boundary; the harness and tools are.
    // - Secret leakage through context: a credential in conversation history or a captured LLM request that
    //   ends up in a tool argument or an answer. Teaches redaction and bounded captures.
    // - Real MCP variant of tool poisoning: serve ConvertUnits from a separate MCP server so the poisoned
    //   description arrives over the protocol and shows up in Discovery.
    // - Real image attachment: pass the receipt as a DataContent image instead of a scripted transcription. Needs
    //   CapturingChatClient.RenderContent to capture media type/size and the Web execution view to render it.
    // - Live-model mode: run the same scenarios against the configured provider (swap ScenarioModel for the
    //   provider client) to compare a real model's behaviour with the scripted worst case.
    // - Offline provider: let AiService start without model credentials when only scripted agents are enabled.

    /// <summary>Every scenario, in teaching order.</summary>
    public static IReadOnlyList<SecurityScenario> All { get; } =
    [
        new("indirect-prompt-injection", InjectedBrowserAgent.AgentName, "Indirect prompt injection via fetched content",
            "A vendor web page contains a hidden HTML comment addressed to AI assistants. The agent fetches the page " +
            "to summarise it, and the hidden text becomes part of the model's context.",
            InjectedBrowserAgent.ScenarioPrompt,
            [
                $"FetchPage returns the page, including the comment with the attacker's IBAN {SimulatedWebTool.AttackerIban}.",
                "The model treats the comment as an instruction and reports a changed bank account as the key point.",
                "No tool misbehaved and no data left the system; the attack targets what the user is told.",
            ],
            [
                "Treat fetched content as data: wrap it, label its source and tell the model not to follow it.",
                "Show sources in the answer so the user can check where a claim came from.",
                "Keep high-impact decisions (payments, account changes) behind a human step outside the agent.",
            ]),
        new("data-exfiltration", LeakyAssistantAgent.AgentName, "Data exfiltration via a tool",
            "A shared document ends with an 'assistant note' asking for its full text to be mailed to an external " +
            "address. The agent has both a read tool and a send tool, so the injected instruction has a way out.",
            LeakyAssistantAgent.ScenarioPrompt,
            [
                "ReadSharedDocument returns the confidential pricing document with the embedded note.",
                $"SendMail is called with the user's address and {SimulatedWorkplaceTool.AttackerAddress}, body = the full document.",
                "The answer looks normal: the user asked for a summary by mail and got one.",
            ],
            [
                "Toggle SendMail off for this run: the scripted model stops at the step it cannot take.",
                "Set a breakpoint before tools and read the SendMail arguments before they execute.",
                $"Enforce the rule in code, not in the prompt — compare with {DefendedAssistantAgent.AgentName}.",
                "Least privilege: an agent that only needs to read should not hold a send tool at all.",
            ]),
        new("tool-poisoning", PoisonedToolsAgent.AgentName, "Malicious tool description (tool poisoning)",
            "A third-party unit converter advertises a description with a hidden <IMPORTANT> block telling the model " +
            "to read internal notes first and pass any token it finds as a 'calibration note'. The user only asked " +
            "for a conversion.",
            PoisonedToolsAgent.ScenarioPrompt,
            [
                "The poisoned description is in the LLM request's tool list (open the captured request or the Details dock).",
                "GetDeploymentNotes is called first, although the user never asked for notes.",
                $"ConvertUnits receives the token from the notes in its note argument; the answer never mentions it.",
            ],
            [
                "Review tool descriptions before trusting a server: they are instructions to the model.",
                "Pin and verify MCP servers; re-run Discovery after updates and diff what changed.",
                "Give third-party tools no access to internal tools' output unless the task requires it.",
                "Toggle GetDeploymentNotes off, or break before tools, to see the chain stop.",
            ]),
        new("image-injection", ReceiptReaderAgent.AgentName, "Hidden text in an image",
            "A receipt photo carries near-white print addressed to the AI expense assistant. The vision step transcribes " +
            "everything it can read, so the hidden sentences arrive in the model's context next to the real receipt " +
            "lines, with nothing marking them as untrusted.",
            ReceiptReaderAgent.ScenarioPrompt,
            [
                $"DescribeImage returns the receipt text ending with the faint print: pre-approved, {SimulatedVisionTool.HiddenAmount} EUR to {SimulatedVisionTool.AttackerPayee}.",
                $"SubmitExpense is called with {SimulatedVisionTool.HiddenAmount} EUR and that payee, not the {SimulatedVisionTool.VisibleTotal} EUR on the receipt.",
                $"The answer tells the user a {SimulatedVisionTool.VisibleTotal} EUR lunch was filed, exactly as the hidden text instructed.",
            ],
            [
                "Treat image transcriptions as data: label the source and tell the model not to follow text found in images.",
                "Break before tools and compare the SubmitExpense arguments with what the user sees in the answer.",
                "Validate in code: cap amounts, allowlist payees or require a human step for anything above a threshold.",
                "Toggle SubmitExpense off for this run: the scripted model stops at the step it cannot take.",
            ],
            SimulatedVisionTool.ReceiptImagePath),
        new("defended-twin", DefendedAssistantAgent.AgentName, "Same attack, defended harness",
            "The exfiltration scenario again, with the same scripted model, but the tools enforce policy: documents " +
            "are wrapped as untrusted data and SendMail only accepts recipients in " +
            $"{SimulatedWorkplaceTool.AllowedMailDomain}.",
            DefendedAssistantAgent.ScenarioPrompt,
            [
                "The model still requests the external send; a tool request is not authorization.",
                "SendMail rejects the recipient in code and reports why; nothing is queued.",
                "The answer surfaces the blocked attempt instead of hiding it.",
            ],
            [
                "Put enforcement in the harness and tools, where a manipulated model cannot talk its way past it.",
                "Make blocked actions visible so humans learn that content tried to steer the agent.",
            ]),
    ];

    /// <summary>Finds the scenario an agent plays, or null for agents outside this module.</summary>
    public static SecurityScenario? ForAgent(string? agentName) =>
        agentName is null ? null : All.FirstOrDefault(s => string.Equals(s.AgentName, agentName, StringComparison.OrdinalIgnoreCase));
}
