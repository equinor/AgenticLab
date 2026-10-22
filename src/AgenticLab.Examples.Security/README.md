# Security Lab example

An optional example module that shows **what can go wrong** in an agent's live flow. Five agents replay
deterministic attack scenarios through the normal AiService harness, so capture, breakpoints, per-run
tool toggles and the Blazor live flow all show the attack step by step.

Everything is simulated in process. The module makes no network requests, sends no mail, runs no
commands, decodes no images and never calls a model provider. Addresses, URLs, payees and the "token"
are placeholders.

## Enable

AppHost enables this module in Development. Use `--Examples:security:Enabled=false` to disable it.
In other environments, or when running AiService and Web standalone, set
`Examples__security__Enabled=true` on **both** services:

```sh
dotnet run --project src/AgenticLab.AppHost -- --Examples:security:Enabled=true
```

Select the **Security Lab** host on `/flow`, pick an agent and press **Use scenario prompt** in the
panel (or the suggested prompt on the empty conversation). AiService still needs the configured
model provider for the other agents; the scripted agents ignore it.

## Scenarios

| Agent | Scenario | What you will see |
| --- | --- | --- |
| `InjectedBrowser` | Indirect prompt injection via fetched content | `FetchPage` returns a vendor page with a hidden HTML comment addressed to AI assistants. The answer reports the attacker's bank account as the key point. No tool misbehaved. |
| `LeakyAssistant` | Data exfiltration via a tool | `ReadSharedDocument` ends with an "assistant note". `SendMail` is then called with the user **and** an external address, with the full document in the body. The receipt is visible in the answer. |
| `PoisonedTools` | Malicious tool description / tool poisoning | `ConvertUnits` carries a hidden `<IMPORTANT>` instruction. The agent first calls `GetDeploymentNotes`, then passes the fake token to the converter as a `note`. The answer hides it. |
| `ReceiptReader` | Hidden text in an image | `DescribeImage` plays the vision step: it transcribes a receipt photo including near-white print addressed to the assistant. `SubmitExpense` is then called with 4870.00 EUR to `Reimbursements Ltd` while the answer reports a 48.70 EUR lunch. The panel shows the receipt and can raise its contrast to reveal the print. |
| `DefendedAssistant` | Defended twin | Same prompt and document as `LeakyAssistant`, but documents are wrapped as untrusted and `SendMail` rejects recipients outside `contoso.example` in code. |

Try these harness controls on the first four:

- Set a **before-tool** breakpoint and inspect the tool arguments before releasing the call.
- Disable the second tool (`SendMail`, `ConvertUnits`, `SubmitExpense`) in the run options; the scripted model reports
  that the step cannot happen and stops without an error.
- Compare the captured LLM request for `LeakyAssistant` and `DefendedAssistant`: the injected text is
  identical; only the tool boundary differs.

The Learn lesson **5 Security/AppSec** links to the exfiltration scenario with a before-tool
breakpoint already set.

## How it works

- [Scripting/ScenarioModel.cs](Scripting/ScenarioModel.cs) is an `IChatClient` that follows a
  [Playbook](Scripting/Playbook.cs): tool calls in order, then an answer composed from the tool results.
  It only requests a tool that is present in the request's tool list, so per-run toggles work.
- Each agent in [Agents](Agents) inherits `ScenarioAgentBase`, which implements the shared
  `IScriptedModelAgent`. `AgentCatalog` wraps the scripted model with `ChatClientProvider.Wrap`, so the
  host pipeline (tool filtering, function invocation, capture, telemetry) is unchanged.
- [Tools](Tools) hold the in-process fakes: `SimulatedWebTool`, `SimulatedWorkplaceTool` (guarded or
  not), `PoisonedThirdPartyTool`, `SimulatedVisionTool` (a scripted transcription standing in for a
  vision model; no image is decoded) and `SimulatedExpenseTool`. Guarded behaviour is code, not prompt text.
- [wwwroot/receipt-0423.svg](wwwroot/receipt-0423.svg) is the viewable receipt, served at
  `_content/AgenticLab.Examples.Security/receipt-0423.svg`. Its near-white lines repeat
  `SimulatedVisionTool.HiddenInstruction`; keep the two in sync (a test checks the key phrases).
- [Scenarios/SecurityScenarios.cs](Scenarios/SecurityScenarios.cs) is the briefing catalogue used by
  the panel and tests. Its `BACKLOG` comment lists further scenarios to add (confused deputy, rogue
  A2A specialist, poisoned skill file, direct jailbreak, secret leakage, a real-MCP variant, a real
  `DataContent` image attachment, live-model mode and an offline provider).
- [Components/SecurityScenarioPanel.razor](Components/SecurityScenarioPanel.razor) is the read-only
  Web panel; it only pre-fills the composer through `ExamplePanelContext.SetDraft`. For a scenario
  with an `ImagePath` it shows the image and a **Reveal hidden text** toggle (a CSS contrast filter).

## Add a scenario

1. Add any fake tool under `Tools/` with `[Description]` methods and an `AsTools()` list.
2. Add an agent under `Agents/` inheriting `ScenarioAgentBase` with a `Playbook` ending in a
   `ScriptedAnswer`, a `Prompt` and a `RiskLevel`/`Guardrails`.
3. Register it in [SecurityExample.cs](SecurityExample.cs) (`AgentNames`, `ToolRisks`, `Resources`,
   `AddAiServices`) and the harness mode in `SecurityHarness`.
4. Add the `SecurityScenario` entry (set `ImagePath` to a `wwwroot/` asset if the attack arrives
   through an image) and a test in [Tests](Tests).

## Tests

```sh
dotnet test src/AgenticLab.Examples.Security/Tests/AgenticLab.Examples.Security.Tests.csproj
```

The module tests run each playbook through `UseFunctionInvocation` with the fake tools and assert the
leak, the blocked twin, tool-toggle behaviour, second-turn follow-ups and streaming parity. The host
test `ScriptedAgentRunsOfflineThroughHostPipelineAndHonoursToolToggles` in
[ExampleExtensionTests](../../tests/AgenticLab.AiService.Tests/ExampleExtensionTests.cs) runs
`LeakyAssistant` through the real `AgentCatalog` without provider credentials.
