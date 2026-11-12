# Agents, personas and the layered harness prompt

Agentic Lab's chat API, agent roster, conversation memory and prompt/model configuration.
See [workspace features](workspace.md) for user-defined agents, skills and instructions, and
[Execution](execution-explorer.md) for streaming and controls. These APIs assume a trusted local
environment, not authenticated multi-user access; see [SECURITY.md](../SECURITY.md).

## Chat API

Use the **aiservice** endpoint shown in the Aspire dashboard for direct API calls. These routes
are implemented in [AgentEndpoints](../src/AgenticLab.AiService/Endpoints/AgentEndpoints.cs) and
[ChatEndpoints](../src/AgenticLab.AiService/Endpoints/ChatEndpoints.cs).

### `GET /agents`

Returns `agents` and a `default` name (`ChatAgent`). Each entry describes tools, workspace/skills
support, model, risk and guardrails, plus optional `exampleId`/`requiresExampleUi` metadata.
Each entry may also carry `suggestedPrompts`: short example messages the agent advertises. Clients offer
them as composer pre-fills (Blazor's empty conversation shows a **Try asking** list; React replaces its
generic suggestions with them) and never send them automatically or as instructions. Code-defined agents
override `SuggestedPrompts` on `AgentDefinitionBase` (default empty); workspace agents declare a
`prompts:` list (see [workspace-defined agents](workspace.md#workspace-defined-agents-the-agents-folder)).
A2A-capable agents also list `remoteAgentNames`, the remote specialists the service bounds them to
(see [protocols](protocols.md#a2a-server-the-orchestrator-agent)); clients should render that list
rather than the global `GET /a2a` roster.
Enabled [examples](examples.md) can add agents; discover the roster rather than hardcoding it.
`POST /agents/workspace` lists workspace-defined agents separately. `GET /examples` returns public
module manifests, not component types or remote prompts. Each agent's `scriptedModel` is `true` when it
replays a scripted model, so a [selected model](#selectable-models) does not apply to it; requests
that send one for it are rejected.

### `GET /models`

Returns the [selectable models](#selectable-models) and the default:

```json
{
  "models": [
    { "id": "gpt-5.6-luna", "name": "GPT-5.6 Luna" },
    { "id": "gpt-4o", "name": "GPT-4o" }
  ],
  "default": "gpt-5.6-luna"
}
```

Only ids and display names are returned, never endpoints or keys. The React BFF forwards this route.

### `POST /chat`

Send a message, optionally choosing an agent (otherwise the default is used):

```json
{ "message": "Who was Alan Turing?", "agent": "WikiAssistant", "model": "gpt-4o" }
```

An example response:

```json
{
  "reply": "Alan Turing was a British mathematician and computer scientist...",
  "agent": "WikiAssistant",
  "conversationId": "example-conversation-id",
  "run": { "model": "gpt-4o", "durationMs": 2140, "inputTokens": 1830, "outputTokens": 214, "modelCalls": 2, "toolCalls": 1 }
}
```

Reuse the returned `conversationId` to continue the conversation. Agent names are case-insensitive.
An empty message, unknown agent, model not on the allow-list or missing/invalid required workspace
returns `400 Bad Request`. `run` reports the model that answered and what the run cost; the streaming
API sends the same object on its `final` event.

Both chat paths support these optional fields:

| Field | Effect |
| --- | --- |
| `agent` | Select an agent; omission uses the default. |
| `model` | Select a model from `GET /models` for every model call of this run and its A2A delegations; omission keeps the agent's own. |
| `workspace` | Existing service-side folder, required by workspace agents. |
| `vendor` | Replace host guidance, not the agent or its tools. |
| `disabledTools` | Narrow the agent's tool set for this run. |
| `disabledSkills` | Disable named skills; available skills default on. |
| `enabledInstructions` | Inject selected workspace instructions; default off. |

### Input limits

`Chat:MaxMessageLength` (default 0, no limit) caps messages and answers to an agent's questions, in
characters. `POST /chat`, `POST /chat/stream` and the `answer` action of `POST /chat/control` return
`400 Bad Request` with "Messages can be at most N characters." when it's exceeded. `GET /chat/limits`
returns `{ maxMessageLength }` (0 when unlimited), so Web stops input at the limit, shows a counter and
disables Send. The Radix deployment sets 500.

### `POST /chat/reset`

Clear remembered history with `{ "conversationId": "example-conversation-id" }`.
Returns `204 No Content`, or `400 Bad Request` for a blank ID. It does not reset an example's
domain state, approve an action or undo tool side effects. For interactive execution, see
the [streaming API](execution-explorer.md#post-chatstream) and
[control API](execution-explorer.md#post-chatcontrol).

## Conversation retention

Agents are stateless; [ConversationStore](../src/AgenticLab.AiService/Application/Conversations/ConversationStore.cs)
keeps an in-memory session per conversation. History survives agent changes but not service restarts.
Use a conversation sequentially; there is no per-conversation locking. Configure expiry in
[AiService settings](../src/AgenticLab.AiService/appsettings.json):

```json
{
  "Conversations": {
    "InactiveTtl": "01:00:00",
    "CleanupInterval": "00:05:00"
  }
}
```

Each use refreshes the sliding expiry. The `AgenticLab.AiService.Conversations` meter reports
`conversations.retained`, `conversations.expired` and `conversations.reset` counts, never content.
Backend retention is independent of [Web replay retention](execution-explorer.md#replay-retention).

## Conversation memory and the layered prompt

**Host changes:** Blazor and React start a fresh conversation and clear captures when Host changes,
preserving drafts and view preferences. Agent-only changes keep history. This is client behavior;
the server does not reset based on `vendor`. React commits the host change after successful reset;
Blazor uses a fresh local ID immediately and reports cleanup failures.

**Prompt layers:** the harness's shared guidance (`<harnessMode>`) precedes its persona
(`<agentMode>`). Harness is the technical name for the host's agent-running machinery. Selecting
a vendor replaces only the host guidance, retaining the persona and configured tool set. It does
not select a different model deployment. Prompts are original teaching examples, not proprietary
vendor prompts or integrations with those products.

`default` preserves the agent's own guidance. Blank/unknown vendor keys and empty overrides also
leave it unchanged. Other hosts require [enabled examples](examples.md#default-startup);
Copilot365 uses module ID `copilot365` but API host key `microsoft365`.

- `GET /vendors` lists registered host names, modes, simulated model labels and optional example metadata.
- `POST /harness` accepts `{ Agent, Vendor }` and returns `{ Prompt }` for the host guidance preview.
- Captured `llm-request` data shows the actual composed instructions sent for a run.

[AgentDefinitionBase](../src/AgenticLab.Extensibility/Agents/AgentDefinitionBase.cs) composes the
layers; [VendorHarnessCatalog](../src/AgenticLab.AiService/Application/Agents/VendorHarnessCatalog.cs)
resolves registered overrides. Since agent instructions are fixed at construction, overrides create
a transient agent on the same chat client instead of mutating the cached agent. For registration
and ownership, see [example modules](examples.md).

## Agents

| Agent | Persona | Tools |
|-------|---------|-------|
| `WikiAssistant` | Concise research helper grounded in Wikipedia. | `SearchWiki`, `GetWikiPage` |
| `MathTutor` | Patient tutor that solves and explains arithmetic. | `Calculate` |
| `TriviaMaster` | Playful trivia host that researches facts and crunches numbers. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `ChatAgent` (default) | Friendly conversational companion that chats from its own knowledge. | _(none)_ |
| `ChatGpt` (opt-in `chatgpt` example) | Conversational assistant used by the ChatGPT demo's chat mode, with Wikipedia grounding and arithmetic. | `SearchWiki`, `GetWikiPage`, `Calculate` |
| `Ask` | Read-only assistant that answers questions and explains code in the workspace without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles` |
| `Plan` | Read-only planner that investigates the workspace and proposes an implementation plan without changing anything. **Requires a workspace.** | `ReadFile`, `ListFiles`, `AskQuestion` |
| `Coder` | Workspace-scoped coding agent that generates and edits code and runs allowlisted user and date/time commands (`whoami`, `date /t`/`time /t` or `date`). **Requires a workspace.** | `ReadFile`, `ListFiles`, `WriteFile`, `DeleteFile`, `RunCommand`, `ReadSkill` |
| `TimeKeeper` | Tells the current time using a tool **discovered from the MCP server** (real MCP client). | `GetCurrentTime` (MCP) |
| `Orchestrator` | Solves arithmetic itself, but delegates general-knowledge/research/creative questions to **specialist agents over the A2A protocol** (real A2A client), routing by name. Its roster is every discovered agent no enabled example claims. | `Calculate`, `DelegateToAgent` (A2A) |

The first definition registered by `AddDemoAgents` is the default. `Ask` and `Plan` never write,
delete or run commands. Which workspace agents are registered depends on the
[workspace mode](workspace.md#workspace-modes): all three in `Local`, only `Ask` and `Plan` in
`ReadOnlySample`, and none in `Disabled`. The Default host lists `Ask` and `Plan` after its other
agents; hosts only show agents the service registered. For custom agents, see the
[workspace format](workspace.md#workspace-defined-agents-the-agents-folder).

### Optional Copilot365 example

Enable `Examples:copilot365:Enabled=true` for `M365Copilot`, `M365Researcher` and `M365Analyst`.
The [module guide](../src/AgenticLab.Examples.Copilot365/README.md) lists their tools. None requires
a workspace or supports skills. Workplace data and `SendMail` are simulated: no Graph calls or mail
delivery. The Medium risk label illustrates sending; confirmation is a persona instruction, not an
enforced approval gate. The researcher's Wikipedia tools still make real public requests.

### Optional Security Lab example

Enable `Examples:security:Enabled=true` (already enabled in AppHost Development) for the
[Security Lab](../src/AgenticLab.Examples.Security/README.md): `InjectedBrowser`, `LeakyAssistant`,
`PoisonedTools`, `ReceiptReader`, `BuildHelper` and the hardened `DefendedAssistant`. Each implements `IScriptedModelAgent`, so
`AgentCatalog` wraps its deterministic `ScenarioModel` (declared `ModelId` `scripted-scenario`) in
the normal host pipeline instead of the configured provider; the scripted model ignores
`Models:ForceDefaultModel` and per-agent model overrides, rejects an explicit `model` with `400`, and costs nothing. Capture, breakpoints,
per-run tool toggles and conversation history behave exactly as for live-model agents. Every page,
document, mail, image transcription, repository file, shell and third-party tool is simulated in-process: no network, mail or process runs.

### ChatGPT lookup and calculation demo

Enable `Examples:chatgpt:Enabled=true` for the [ChatGPT example](../src/AgenticLab.Examples.ChatGpt/README.md)
(already enabled in AppHost Development). Its **chat** mode uses `ChatGpt`; the tool-free default
remains `ChatAgent`. Direct callers select both `agent: "ChatGpt"` and `vendor: "chatgpt"`.

Try: "Find the height of the Eiffel Tower on Wikipedia, then calculate how much taller it is than
a 250-metre building." Wikipedia uses its public API; calculation runs locally, with no extra key.
`GetWikiPage` returns a summary, not a full article or general web search. Retrieved text is source
material, not instructions. The model chooses calls; the host executes permitted ones.

## Asking the user a question (human-in-the-loop)

`Plan` includes [AskQuestion](../src/AgenticLab.AiService/Application/Tools/AskQuestionTool.cs);
workspace agents can select it too. On `/chat/stream`, an `ask-question` event displays an answer
input. Submit `POST /chat/control` with action `answer` and the `answer` text to resume. The answer
becomes a tool result in conversation history. Several questions may occur in one run.

There is no answer timeout: the run waits until answered, stopped or cancelled. Non-interactive
`POST /chat` returns a tool fallback asking the model to proceed with stated assumptions instead
of blocking. [UserInputScope](../src/AgenticLab.AiService/Application/Flow/UserInputScope.cs)
owns the per-run wait.

## Toggling an agent's tools (per run)

Send `disabledTools` with case-insensitive tool names. This only narrows the declared set; it cannot
grant tools. Web exposes checkboxes in Settings (reset on agent change).

[ToolFilteringChatClient](../src/AgenticLab.AiService/Application/Flow/ToolFilteringChatClient.cs)
filters before function invocation so disabled tools reach neither model nor invocation loop.
Do not implement subtraction through per-run `ChatOptions.Tools`: the framework unions that list
with agent tools. [RunScopeSet](../src/AgenticLab.AiService/Application/Flow/RunScopeSet.cs) keeps
per-run filters together in both chat paths.

## Model providers

Select one inference provider at startup with `Models:Provider`. Values are case-insensitive;
missing or blank selects `AzureOpenAI`. Unsupported values fail configuration validation, never
silently fall back to a different provider. Only the selected provider's settings are required:

| Provider | Required settings | Model meaning |
| --- | --- | --- |
| `AzureOpenAI` (default) | `AzureOpenAI:Endpoint`, `AzureOpenAI:ApiKey`, `AzureOpenAI:Deployment` | Your Azure deployment name, including Foundry-managed deployments. |
| `OpenAI` | `OpenAI:ApiKey`, `OpenAI:Model` | A chat-completions model available to your OpenAI API account. |
| `Gemini` | `Gemini:ApiKey`, `Gemini:Model` | A Gemini API model supporting chat and function calling. |

Use [AppHost user-secrets or environment variables](../README.md#configure-a-model-provider).
AppHost explicitly forwards the selected settings to AiService and A2AServer, not to Web, BFF or
MCP. Standalone inference hosts accept the same configuration. Restart after changing providers,
models or credentials. There is no per-user key entry, runtime provider switch or automatic fallback;
within the provider, a run may choose one of the [selectable models](#selectable-models).

[ModelConnectionOptions](../src/AgenticLab.ModelProviders/ModelConnectionOptions.cs) validates keys,
model names, flags and the Azure endpoint without including credential values in validation errors.
[ModelClientFactory](../src/AgenticLab.ModelProviders/ModelClientFactory.cs) creates raw `IChatClient`
adapters. AiService retains its tool filtering, invocation, capture and telemetry pipeline; A2A keeps
its own invocation and telemetry pipeline. Concrete SDK dependencies stay in this shared runtime
library, outside the frontend and example contracts.

OpenAI uses `https://api.openai.com/v1/`; Gemini uses
`https://generativelanguage.googleapis.com/v1beta/openai/`. Native endpoints are fixed, not arbitrary
proxy URLs. Gemini's compatibility API is beta. Its adapter retains opaque tool-call `extra_content`
(including thought signatures) with the conversation and preserves it across streamed chunks and
tool-result requests. It does not interpret those signatures or store them in a global cache.
Other Gemini-specific APIs and OpenAI Responses features are outside this chat-completions integration.

Host branding and illustrative model labels remain independent of provider selection. Enabling the
Gemini example does not connect to Google; setting `Models:Provider=Gemini` does. API billing is
separate from consumer subscriptions, and live calls transmit context to the selected provider.

## Selectable models

Users can choose the model behind a conversation to compare answer quality, speed and tool calling.
The allow-list is the selected provider's `{Provider}:Choices`: a comma-separated list of model or
deployment ids, each optionally followed by `=Display name`. Names are otherwise derived from the id
(`gpt-5.6-luna` becomes "GPT-5.6 Luna"). The provider default (`AzureOpenAI:Deployment` or
`{Provider}:Model`) is always selectable and preselected; it is added first when not listed. Empty ids
or duplicates fail at startup. Azure ids match case-insensitively; OpenAI and Gemini ids are
case-sensitive.

Model names are not secrets, so the Azure defaults are committed once in
[models.json](../src/AgenticLab.ModelProviders/models.json), embedded in `AgenticLab.ModelProviders` and
loaded by both AiService and A2AServer through `AddModelDefaults()`:

```json
{
  "AzureOpenAI": {
    "Deployment": "gpt-5.6-luna",
    "Choices": "gpt-5.6-luna, gpt-5.6-terra, gpt-5.6-sol, gpt-4o"
  }
}
```

It is the lowest-precedence source: appsettings, [user-secrets](../README.md#choose-selectable-models),
environment variables (`AzureOpenAI__Choices`) and the command line override it. AppHost forwards an
override to both hosts, which must hold the same list because both validate it. OpenAI and Gemini ship
no list, so they offer only their configured `Model` unless `OpenAI:Choices` or `Gemini:Choices` is set.

`POST /chat` and `POST /chat/stream` reject a `model` outside the list with `400 Bad Request` before
anything runs. A valid selection is held in a per-run
[ModelSelectionScope](../src/AgenticLab.AiService/Application/Models/ModelSelectionScope.cs).
Each agent's chat client routes every round-trip of the run through
[ModelRoutingChatClient](../src/AgenticLab.ModelProviders/ModelRoutingChatClient.cs) to that model,
so one agent pipeline serves all models. A2A delegations send the selection in the
`X-AgenticLab-Model` header; the A2A server checks it against its own list (`400` otherwise) and
answers on the same model.

**Which model wins**, from highest to lowest:

1. A scripted agent (`IScriptedModelAgent`, such as the Security Lab) always uses its own scripted model.
   A request that names a `model` for it returns `400 Bad Request`, so no caller mistakes a scripted
   answer for the chosen model's.
2. The run's selected `model`. It overrides `Models:ForceDefaultModel`, per-agent overrides and an
   agent's `ModelId`. The allow-list is the operator's control over which models can run.
3. Without a selection, `Models:ForceDefaultModel` runs the provider default.
4. Otherwise the [per-agent model](#per-agent-models) chain.

Blazor and React always send the user's choice when the service lists models, so per-agent and
forced models only apply to direct API callers that omit `model`. Both frontends keep the choice under
the `agenticlab-model` browser key, apply it to every following message until changed, and label
each answer with the model that produced it. Changing model keeps the conversation, so the next model
sees the earlier answers in its history.

**Telemetry:** every completed run records, by `agent`, `host` and `model`, the
`agenticlab.chat.duration` and `agenticlab.chat.model_duration` histograms (seconds), `agenticlab.chat.tokens` (with `token.type` `input` or
`output`), and the `agenticlab.chat.model_calls` and `agenticlab.chat.tool_calls` histograms on the
`AgenticLab.AiService.Chat` meter. The request span is tagged with `gen_ai.request.model`,
`gen_ai.usage.input_tokens`, `gen_ai.usage.output_tokens`, `agenticlab.model_calls`,
`agenticlab.tool_calls`, `agenticlab.duration_ms` and `agenticlab.model_duration_ms`. Each model call is also traced per model by the
Microsoft.Extensions.AI OpenTelemetry client. Model tags come only from configured ids or `scripted-*`
labels. Token counts cover the local agent; A2A specialists report their own calls from A2AServer.
Durations include tool execution and any stepping pauses. Model duration (`modelDurationMs` in the run
summary) counts only the time spent waiting for the local model, excluding tools, breakpoints and
manual stepping, so it stays comparable in any mode. Each captured `llm-request` records the `model`
it was routed to, and each `llm-response` detail ends with that round-trip's model time. An
`llm-response` also carries that round-trip's own `usage` (`inputTokens`, `outputTokens`); the run
summary is the sum over all round-trips. The Prompt signature shows that sum for a delivered exchange
and a single round-trip's `usage` at a model turn. `usage` is set
only when the provider reported both counts, so a reported zero is kept and missing or partial usage
is null rather than zero. `cachedInputTokens` is the part of the input the provider read from its prompt
cache; it is null when the provider does not report cache usage and is not added to the run summary.

## Per-agent models

Agents can select different models within the single configured provider, sharing its credentials.
[ChatClientProvider](../src/AgenticLab.AiService/Application/Agents/ChatClientProvider.cs) caches one
client per model; native identifiers are case-sensitive, while Azure retains case-insensitive caching.
When a run selects no model, the declared model is resolved in this order:

1. `Agents:{Name}:Model` in AiService configuration.
2. `Agents:{Name}:Deployment`, **only for Azure OpenAI**.
3. The agent's `ModelId`, or a workspace agent's `model` field.
4. The selected provider's default model or deployment.

AppHost forwards explicitly configured agent model overrides to AiService. These may also be set
directly in [AiService settings](../src/AgenticLab.AiService/appsettings.json). For example:

```json
{
  "Agents": {
    "Coder": { "Model": "<model-id-for-the-selected-provider>" }
  }
}
```

The existing `Agents:Coder:Deployment` sample is ignored for OpenAI/Gemini, so switching providers
does not send an Azure deployment name to another API. Explicit `Model` and workspace `model` values
are not translated; update them for the provider you select. A2A specialists use the provider's
global default, or the run's selected model, without per-specialist model overrides.

`GET /agents` exposes the resolved **declared** model as `ModelId`. Web shows the run's selected model
instead, rather than claiming that host branding or a model name proves which provider executes.

**Display versus execution:** `Models:ForceDefaultModel=true` makes every local agent execute on the
provider's global default while leaving declared model labels unchanged. When the neutral setting is
absent, Azure honors `AzureOpenAI:ForceDefaultModel` as before; OpenAI/Gemini default to `false` and
ignore the Azure flag. An explicit neutral `false` also overrides a legacy Azure `true`.

The shipped AiService Azure settings enable the legacy flag. This existing example still displays
Coder's `gpt-5.3-codex` but, when no model is selected, runs it on the default `gpt-5.6-luna`:

```json
{
  "AzureOpenAI": {
    "ForceDefaultModel": true
  },
  "Agents": {
    "Coder": { "Deployment": "gpt-5.3-codex" }
  }
}
```

## Provider verification

`ModelProviderTests` uses injected HTTP transports with fake keys to check endpoint/authentication,
ordinary and streaming tool loops, serialized Gemini signatures, cancellation, configuration and
model precedence. `ModelSelectionTests` covers the allow-list, selection precedence, usage accounting
and endpoint rejection. `ProtocolIntegrationTests` also delegates through each factory-backed adapter on
loopback A2A servers, including a selected model forwarded to, and rejected by, the A2A server.
These tests do not contact model providers or prove live model availability:

```sh
dotnet test tests/AgenticLab.AiService.Tests/AgenticLab.AiService.Tests.csproj --filter "FullyQualifiedName~ModelProviderTests|FullyQualifiedName~ModelSelectionTests|FullyQualifiedName~ProtocolIntegrationTests|FullyQualifiedName~FlowExecutionTests"
```

For live verification, explicitly configure development credentials, start AppHost, and try a short
chat, a streamed calculator request, a Wikipedia lookup and Orchestrator delegation for each provider
and each selectable model. Use synthetic input; these checks incur normal provider usage. Record the
provider and model actually tested.
