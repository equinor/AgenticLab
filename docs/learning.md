# In-app learning (Learn panel + guided journey)

Agentic Lab has two learning surfaces: a contextual **Learn** panel on the
[Flow page](web-flow-page.md) at `/flow` and a standalone **Agent guide** at `/learn`.
The front page at `/` offers direct links to both experiences.

Both use **Agent = Harness + Model**. The harness manages context, instructions, tools, memory and
execution controls; the model reasons and chooses an answer or next step. A tool request is not
permission: the harness checks and executes permitted actions. **Harness** means the
agent-running software, not the whole agent or a particular machine. Memory is retained state;
context is the input selected for a model request.

## In-app learning content (Learn panel)

Enable **Learn** in the Flow header to show contextual info buttons and the right-hand topic panel.
It starts off. Choose a topic from the index or an info button in the diagram; **All topics** returns
to the index. Topics cover core concepts and products, including concepts without a diagram node.
For example, Client appears in the index but not in the host anatomy because it sits outside the host.

Opening a concept enables Learn and expands its dock. The dock can be resized or collapsed;
turning Learn off hides it and clears the selected topic. It is independent of the read-only
**Details** dock, so both can remain open. Product info beside the App selector comes from the
enabled example's metadata; Default has no product topic.

## Guided agent learning

Open **Agent guide** from Home or Flow in the same tab; **Live flow** opens `/flow` in that tab.
The header's product mark returns Home. From Discovery, the guide opens in a new tab.
Both Home and the guide work without AiService or model credentials:

```sh
dotnet run --project src/AgenticLab.Web
```

Choose **Agent guide** at the printed Web URL, or visit `/learn` directly. Lessons are local explanations, not live telemetry or
deployment/security maps. Their controls make no model, tool, discovery, scheduling or deployment
calls. Live-flow links navigate without sending a prompt. For Discovery, use the
[Flow overlay or standalone page](protocols.md#discovery-visualization-mcp--a2a); Learn has no
Discovery entry.

## Agent guide

The guide opens at **Intro** and follows four root topics, with eight subtopics grouped under
**Agent**, **One foundation, many systems** and **Where agents run**.
The guide has twelve lessons; existing lesson IDs remain unchanged. Bookmark any stage with
`/learn?stage=<id>`; reload and browser Back/Forward preserve navigation. Hidden or unknown IDs
fall back to the first stage. The product-specific `map-to-foundry` definition is hidden.

| Number | Topic | ID | Focus |
| --- | --- | --- | --- |
| 1 | Intro | `why-agents` | The user and chat application, then the LLM, ChatGPT with web search, and the harness + model. |
| 2 | Agent | `model-to-agent` | Agent = Harness + Model, with each part's responsibilities. |
| 2.1 | Context management | `inside-the-harness` | Instructions, task, conversation history, tool results and tool descriptions selected for a model request. |
| 2.2 | Model response | `tool-calling` | Answer text, tool-call requests and how the harness handles them; links to Live flow. |
| 2.3 | The agent loop | `agent-loop` | The generic cycle, its result return path and the response-or-stop exit, without a task example. |
| 2.4 | Anatomy of an agent | `anatomy-of-agent` | Distinguish text supplied to the model from tool configuration and executable controls; existing configurations are unchanged. |
| 3 | One foundation, many systems | `agents-everywhere` | Compare purposes, local/cloud settings and user/schedule/event triggers. |
| 3.1 | Different purposes, same pattern | `agent-landscape` | Chat, coding, office and custom purposes share one foundation. |
| 3.2 | Connecting tools and agents | `wider-ecosystem` | MCP tool calls versus A2A delegation. |
| 4 | Where agents run | `where-to-run` | Personal runtime, existing product, own service or managed agent platform. |
| 4.1 | What can go wrong | `what-can-go-wrong` | Injected content, data exfiltration through a tool and poisoned tool descriptions; the prompt is not a security boundary. **Show in live flow** deep-links to the Security Lab's exfiltration scenario with a before-tool breakpoint (requires the optional `security` example). |
| 4.2 | Run and improve | `run-and-improve` | Run, observe, evaluate and improve across versions, not within one turn. |

Shared foundations do not imply identical capabilities, tools or permissions. Hosting is a separate
choice: a local harness can use a remote model, and observation and evaluation matter wherever it runs.

### Navigation and reveals

Chapter navigation and lesson reveals are separate. **Previous/Next**, **Show complete diagram**
where available, and **Restart** control the current lesson. There is no autoplay. Chapter changes
through links, browser history or reloads reset progress; opening a related concept preserves it.
Restart preserves example selections. Intro returns to **Talking with a chat application** when revisited through a link.

Intro has four short reveals: **Talking with a chat application**, **How it actually looks like**,
**How it looks like today** and **From chat to agent**. The first shows only the user-facing conversation. The second
introduces the model that generates the response. The third shows that current ChatGPT can look
up information online before answering, with the application managing the conversation and searches.
The fourth introduces ChatGPT as an agentic chat application and names the part that manages context
and tool use as the harness. Its takeaway below the diagram reads: **Together, the harness and the
model form the agent.** This connects Intro to the next chapter. Intro has no related concept links; tool calls, execution checks,
context management and memory are covered in detail later.

Each Intro reveal has a static connection diagram beneath its text. The first shows **User** and
**Chat application**, connected by **Message / Reply** arrows. This is a view of the conversation,
not a claim that the application generates the reply without a model. The second adds **LLM**
and **Request / Response** arrows, keeping the user and application in place. The third identifies
the application as **ChatGPT** and adds **Web search** beneath it, with **Search / Results** arrows.
This abstract search capability does not claim a particular model version, search provider or that
every answer requires a search. These are simplified illustrations, not live integrations.
The fourth keeps **Chat application** and **ChatGPT** as the middle box's labels and expands it
downward around the search area. A **Harness** section inside the application contains **Web search**
and its **Search / Results** arrows. The user and LLM stay in place outside the application.
This groups the application's search capability, not an external search provider's infrastructure.
There is no sample question or answer, changing text inside boxes, timed highlight or playback control.
Earlier diagrams are replaced, not accumulated; Previous, Next, **Show complete diagram** (jumps to
the fourth reveal), Restart and keyboard navigation also update the diagram. An accessible list describes the connections, not a numbered execution
sequence. The guide does not make model or search calls.

**Left/Right arrow keys** combine reveal and lesson navigation. Right advances one reveal;
after the last reveal it opens the next lesson at its first reveal. Left goes back one reveal;
from the first reveal it opens the previous lesson at its final reveal. Both directions visit
all eleven lessons, including subtopics: Agent advances directly to the first reveal of `2.1`, not to `3`.
Lessons without reveal controls take one key press to leave. The journey does not wrap at either
end. Backward entry shows the loop's final explanation, not **Show all steps**. Lesson URLs change
only when crossing a lesson boundary; reveal progress is not saved in the URL.

The shortcuts work while reading or focusing ordinary buttons and links. Editable fields,
selectors and other controls with their own arrow-key behavior keep their native keys, as do
modified arrows such as Alt+Left/Right for browser history. Held-key repeats do not advance the
guide. Stage changes focus the lesson heading; hiding a focused reveal restores that focus.

The navigation nests **Context management**, **Model response**, **The agent loop** and **Anatomy of an agent** beneath
**Agent**, with matching `2.1`-`2.4` lesson labels. **Different purposes, same pattern** and
**Connecting tools and agents** sit beneath **One foundation, many systems** as `3.1`-`3.2`;
**What can go wrong** and **Run and improve** sit beneath **Where agents run** as `4.1`-`4.2`. Page-level **Previous/Next** still
visits every lesson in order, including the subtopics; the progress counter counts all twelve lessons.

**Agent** is a single view of **Agent = Harness + Model**. Two parts describe their responsibilities:
the harness supplies context, calls the model and executes permitted tool requests; the model
interprets the supplied input and generates an answer or tool request. The model does not execute
tools itself. The root does not repeat Intro's conversation diagram or use reveal controls.
The child lessons expand on context management, model responses and the loop.

**Context management** has six cumulative reveals: an overview, **Instructions**, **Tool descriptions**,
**Task**, **Conversation history** and **Tool results**. Each part has a separate,
short description. The task is the current request; conversation history is selected earlier
messages; tool results are returned content selected for the next request. Earlier parts remain
visible. Memory is omitted for now. This lesson does not include execution, a worked task or
example toggles.

**Model response** has two numbered steps: **Model response is: Answer**, then
**Model response is: Tool Call**. The model reasons over supplied
context and chooses the next action to fulfill the request: answer directly with text or request
a tool call from the harness. Both steps show a Harness box and an LLM
box, with top-aligned content and compact spacing. Context is sent to the LLM and its response
returns to the harness. In Answer, the model determines that it can answer directly without
requesting anything further from the harness, which returns the answer to the user. Tool call places an example
Web search tool ("Finds online information") inside the harness box, with call/result arrows. The harness checks and invokes the requested
tool, then passes the result to context management for the next request. This is a conceptual diagram,
not a task walkthrough or a claim that every tool runs in the harness process. Previous/Next, Restart
and Left/Right keyboard navigation switch the two steps; backward entry starts at Tool call.
A tool request is not execution or permission.

**Show in live flow** opens a [deep link](web-flow-page.md#deep-links) to `/flow` with the
Wikipedia agent selected, the technical view applied, both Wikipedia tools switched off for the run
and a general-knowledge question pre-filled in the composer, so the model answers directly rather
than requesting a tool. It does not start a run: the user still presses Send. Other lessons' **Open
live flow** links remain plain `/flow`. All of these open in a new tab so the guide stays in place. The previous
Wikipedia walkthrough is no longer displayed; its fixture remains available to existing tests.

**The agent loop** connects Context management and Model response from the preceding lessons.
It keeps four nodes and their paths visible: **Context management**, **Model response**,
**Harness execution** and **Answer**. Its eight conceptual steps construct context, generate
a response, handle the response, execute a permitted tool, construct the next context, generate
the next response, pass an answer to the user through harness execution and end the turn.
The model chooses an answer or a tool-call request; the harness checks requests,
executes permitted calls and passes results to context management for the next model request. **Next step**
updates the reached node and the explanation below; unreached nodes have dashed borders and no
step detail. The active node is outlined and traversed paths are highlighted. Results return to
context management; answers and stops exit through harness execution. An answer can end the turn
without a tool call. Cancellation, a limit or a failure can also stop it. Ending does not establish
success. **Show all steps** displays all eight explanations;
Restart and reentry return to context assembly. No task fixture or outcome selector appears here.
The earlier meeting-notes fixtures remain available to unit tests, not as this lesson's example.

Worked examples for these subtopics are deferred. This refresh does not add or change
Anatomy's existing configurations or interactions.

The content introduces one concept at a time, using explicit actors, inputs and outputs.
More detail is available on demand rather than through a terminology-heavy first screen.

The lesson and navigation scroll independently; navigation becomes a horizontal strip on narrow
screens. Reveal controls stay visible while scrolling. Unrevealed content is excluded from focus
and assistive technology; reduced motion disables effects, not manual progression. Both learning
surfaces reuse the [Blazor design system](design-system.md).

### Illustrative configurations

**Anatomy** defaults to the Chat configuration. It builds up standing
instruction text, registered tools, role instructions, selected tools, model choice, execution
controls, task and custom instructions, then optional skill-description and skill-body reveals.
Each reveal adds one node; tools, model and controls have separate steps.
Cards keep fixed grid positions: the system prompt sits beside the registered tools, followed by
role instructions, selected tools and a **Settings** card whose execution controls are revealed as
their own step. Unrevealed cards take no layout space. The full system-prompt text and the model-selection
considerations are shown inline. **Chat / Office / Coding / Custom** tabs change the
example, not the selected backend agent.
Reading a skill supplies instruction text, not permission or an executed workflow.
Neither model choice, persona, instructions nor skills grant permissions.

- Office contrasts a meeting assistant's confirmed communications with a read-only document reviewer.
- Coding offers Ask, Plan, Implement and Review. Only Implement includes writes and terminal use,
  with approval or allowlisting, resource limits and no production access.
- Chat researches questions; Custom reads equipment telemetry and manuals without controlling equipment.

Changing purpose selects its first mode without restarting reveals. Restart keeps the selection;
reentering Anatomy restores Chat. These are original teaching examples, not vendor prompts,
product replicas or claims about native connectors and enforcement.

**Hosting** separates the harness, model service, tools/data access and operational ownership.
Trigger and readiness selections are independent of hosting and reveal progress. Restart keeps
selections; chapter reentry resets them. A local host can use a remote model, and moving to cloud
does not guarantee portability or tool access.

The expandable **Vendors and stacks** entries link to official sources and distinguish frameworks,
products and managed runtimes. Offerings can span categories. Sources were checked on
17 September 2026; preview, licensing and access caveats do not guarantee organizational availability.
Changing category collapses entry details without resetting the lesson. Foundry hosting and managed
evaluation integration are future directions, not capabilities implemented by the guide.

## Authoring and verification

Each topic lives in [Concepts](../src/AgenticLab.Web/Concepts) as a `Concepts/<id>` folder containing:

```text
meta.json   { "title", "summary", "category", "links": [{ "label", "url" }] }
body.md     Markdown explanation
```

[ConceptCatalog](../src/AgenticLab.Web/Services/ConceptCatalog.cs) loads topics at startup and renders
Markdown with Markdig. Edit the files and restart Web. Categories are `core` and `product`; adding
a topic makes it available in the index, with an optional contextual `OpenConcept("<id>")` action.
Preserve existing concept IDs such as `harness` and `llm` and the stage IDs above.

Follow the [authoring rules](../src/AgenticLab.Web/Concepts/AUTHORING.md): summaries and general
explanations are product-agnostic. Put Agentic Lab-specific details in one trailing
`In this application (Agentic Lab)` section. Product topics are the exception.

[AgentLearningJourney](../src/AgenticLab.Web/Learning/AgentLearningJourney.cs) owns stages and topic
references. [ToolCallingLesson](../src/AgenticLab.Web/Components/Pages/LearningParts/ToolCallingLesson.razor)
renders the two-step Model response lesson under the preserved `tool-calling` stage ID;
[ToolCallingStory](../src/AgenticLab.Web/Learning/ToolCallingStory.cs) retains the earlier Wikipedia fixture.
[AgentTurnStory](../src/AgenticLab.Web/Learning/AgentTurnStory.cs) retains the meeting-notes fixtures
and defines the conceptual `AgentLoopStory`; [FoundationStory](../src/AgenticLab.Web/Learning/FoundationStory.cs) and
[HostingStory](../src/AgenticLab.Web/Learning/HostingStory.cs) own illustrative content and reveal
state. Keep source-review dates aligned with the curated hosting list when rechecking it.

Tests cover stage URLs, navigation, reveal/reset behavior and topic references:

```sh
dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj
```

For backend-free desktop interaction and screenshot checks, run
`node tools/web-smoke.mjs --learn-only` against the standalone Web URL using the temporary
Playwright setup in [tools/README.md](../tools/README.md). This checks keyboard reveals, cumulative
nodes, conceptual loop paths and Anatomy configurations without model or tool calls. Journey unit
tests cover the current lesson order, context reveals and eight-step loop. The browser script still
contains selectors for the earlier Wikipedia walkthrough and harness lesson; update those before
final browser verification. Browser checks remain deferred during fast Learn iteration.
