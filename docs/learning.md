# In-app learning (Learn panel + guided journey)

Agentic Lab has two learning surfaces: a contextual **Learn** panel on the
[Flow page](web-flow-page.md) and a standalone **Agent guide** at `/learn`.

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
**Details** dock, so both can remain open. Product info beside the Host selector comes from the
enabled example's metadata; Default has no product topic.

## Guided agent learning

Open **Agent guide** from Flow in the same tab; **Live flow** returns to Flow in that tab.
From Discovery, the guide opens in a new tab. The guide also works without AiService or Azure
credentials:

```sh
dotnet run --project src/AgenticLab.Web
```

Visit `/learn` at the printed Web URL. Lessons are local explanations, not live telemetry or
deployment/security maps. Their controls make no model, tool, discovery, scheduling or deployment
calls. Live-flow links navigate without sending a prompt. For Discovery, use the
[Flow overlay or standalone page](protocols.md#discovery-visualization-mcp--a2a); Learn has no
Discovery entry.

## Agent guide

The guide opens at **Intro** and follows four root topics, with six subtopics grouped under
**Agent**, **One foundation, many systems** and **Where agents run**.
The ten lessons retain their reading order. Bookmark any stage with
`/learn?stage=<id>`; reload and browser Back/Forward preserve navigation. Hidden or unknown IDs
fall back to the first stage. The product-specific `map-to-foundry` definition is hidden.

| Number | Topic | ID | Focus |
| --- | --- | --- | --- |
| 1 | Intro | `why-agents` | Purpose and bounded autonomy, then what makes an agent possible. |
| 2 | Agent | `model-to-agent` | Harness + model and their responsibilities, shown together. |
| 2.1 | Inside the harness | `inside-the-harness` | Context management and execution, then four context aspects with examples on demand. |
| 2.2 | The agent loop | `agent-loop` | Follow a meeting-notes task through model requests, a checked file read, returned content and a response. |
| 2.3 | Anatomy of an agent | `anatomy-of-agent` | Distinguish text supplied to the model from tool configuration and executable controls. |
| 3 | One foundation, many systems | `agents-everywhere` | Compare purposes, local/cloud settings and user/schedule/event triggers. |
| 3.1 | Different purposes, same pattern | `agent-landscape` | Chat, coding, office and custom purposes share one foundation. |
| 3.2 | Connecting tools and agents | `wider-ecosystem` | MCP tool calls versus A2A delegation. |
| 4 | Where agents run | `where-to-run` | Personal runtime, existing product, own service or managed agent platform. |
| 4.1 | Run and improve | `run-and-improve` | Run, observe, evaluate and improve across versions, not within one turn. |

Shared foundations do not imply identical capabilities, tools or permissions. Hosting is a separate
choice: a local harness can use a remote model, and observation and evaluation matter wherever it runs.

### Navigation and reveals

Chapter navigation and lesson reveals are separate. **Previous/Next**, **Show complete diagram**
where available, and **Restart** control the current lesson. There is no autoplay. Chapter changes
through links, browser history or reloads reset progress; opening a related concept preserves it.
Restart preserves example selections. Intro returns to **Purpose** when revisited through a link.

**Left/Right arrow keys** combine reveal and lesson navigation. Right advances one reveal;
after the last reveal it opens the next lesson at its first reveal. Left goes back one reveal;
from the first reveal it opens the previous lesson at its final reveal. Both directions visit
all ten lessons, including subtopics: Agent advances directly to the first reveal of `2.1`, not to `3`.
Lessons without reveal controls take one key press to leave. The journey does not wrap at either
end. Backward entry shows the loop's final explanation, not **Show all steps**. Lesson URLs change
only when crossing a lesson boundary; reveal progress is not saved in the URL.

The shortcuts work while reading or focusing ordinary buttons and links. Editable fields,
selectors and other controls with their own arrow-key behavior keep their native keys, as do
modified arrows such as Alt+Left/Right for browser history. Held-key repeats do not advance the
guide. Stage changes focus the lesson heading; hiding a focused reveal restores that focus.

The navigation nests **Inside the harness**, **The agent loop** and **Anatomy of an agent** beneath
**Agent**, with matching `2.1`-`2.3` lesson labels. **Different purposes, same pattern** and
**Connecting tools and agents** sit beneath **One foundation, many systems** as `3.1`-`3.2`;
**Run and improve** sits beneath **Where agents run** as `4.1`. Page-level **Previous/Next** still
visits every lesson in order, including the subtopics; the progress counter counts all ten lessons.

Inside the harness has six cumulative reveals: **Context management**, then **Execution**, then
**Instructions**, **Context**, **Tool descriptions** and **Memory** beneath Context management.
Earlier nodes remain visible. Context management is a static parent; the other five nodes each
toggle an independent example when clicked or activated with Enter/Space. On desktop the box
appears to that node's right; constrained layouts stack the example beneath its node.
Examples use the loop's meeting-notes task and distinguish guidance from enforced controls,
tool descriptions from implementations, and retained memory from selected context.
Examples start closed, can remain open independently and do not advance the reveals.
Earlier examples stay open when advancing; stepping back clears examples for unreached nodes.
**Show complete diagram** reveals all nodes without opening examples. Restart and lesson reentry
close examples. Unreached nodes and closed examples take no focus or assistive-technology space.

Agent shows **Agent = Harness + Model** immediately, with two matching responsibility boxes.
The harness handles input, manages context and controls execution; the model generates responses
from the supplied input. This overview has no tool example, exchange paths or reveal controls.

The loop keeps four boxes and their connecting paths visible from the start: **Context management**,
**Model**, **Execution** and **Response or stop**. **Next step** reveals the latest reached step inside
its box and shows its explanation below; unreached boxes show only their headings with dashed borders.
The active box is outlined, and traversed paths are highlighted. Tool results return from Execution
to Context management, which assembles the next model request. Responses and stops exit through
Execution. On narrow screens the exit sits below Execution. **Messages and tool definitions** expands the supplied
request content. **Show all steps** reveals all reached boxes and the full sequence of explanations;
**Restart** clears the reveals back to the user message. Changing stages or reloading resets the sequence.
Unreached steps remain unrevealed; Execution still handles completion when notes are already supplied.

The first model request contains instructions, the task, selected session state and a `read_file`
description, but no file content. A new session has no earlier messages. Tool descriptions are
model input, not invocations. Execution applies configured checks before every tool invocation,
including a request from the first model response. Only after Context management includes the result
in a second request does the model receive the notes. The answer preserves unstated owners and
deadlines as **not stated**. Retention is separate from selecting context, and does not retrain
the model. Responses can include text, tool requests or both; ending a run does not prove success.

**Other outcomes** is optional and initially closed: denied access, a missing file, cancellation,
a zero tool-call limit, or notes already supplied by the user. Selecting a case resets its steps;
Restart preserves the selection. Denied/stopped cases never invoke a file tool; a missing-file
case invokes it but returns an error. These are synthetic examples, not live operations, provider
API formats, production policies or a filesystem sandbox.

The content introduces one concept at a time, using explicit actors, inputs and outputs.
More detail is available on demand rather than through a terminology-heavy first screen.

The lesson and navigation scroll independently; navigation becomes a horizontal strip on narrow
screens. Reveal controls stay visible while scrolling. Unrevealed content is excluded from focus
and assistive technology; reduced motion disables effects, not manual progression. Both learning
surfaces reuse the [Blazor design system](design-system.md).

### Illustrative configurations

**Anatomy** defaults to the same read-only meeting-notes task as the loop. It builds up standing
instruction text, registered tools, role instructions, selected tools, model choice, execution
controls, task and custom instructions. Each reveal adds one node; tools, model and controls have
separate steps. Only `read_file` is enabled for this example, with the permitted path
`meeting-notes.txt`. No skill-loading, file-writing or terminal tool is enabled.
Unrevealed nodes take no layout space. Full system-prompt text and **Other agent configurations**
start collapsed. **Meeting notes / Chat / Office / Coding / Custom** changes the example, not the
selected backend agent. Model-selection considerations are available on demand.
The other configurations add optional skill-description and skill-body reveals when skills are
configured; reading a skill supplies instruction text, not permission or an executed workflow.
Neither model choice, persona, instructions nor skills grant permissions.

- Office contrasts a meeting assistant's confirmed communications with a read-only document reviewer.
- Coding offers Ask, Plan, Implement and Review. Only Implement includes writes and terminal use,
  with approval or allowlisting, resource limits and no production access.
- Chat researches questions; Custom reads equipment telemetry and manuals without controlling equipment.

Changing purpose selects its first mode without restarting reveals; switching to a shorter
configuration clamps progress to its last step. Restart keeps the selection;
reentering Anatomy restores Meeting notes / Action list. These are original teaching examples, not vendor prompts,
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
references; [AgentTurnStory](../src/AgenticLab.Web/Learning/AgentTurnStory.cs) owns the synthetic
meeting-notes trace, responsibility examples and outcome navigation; [FoundationStory](../src/AgenticLab.Web/Learning/FoundationStory.cs) and
[HostingStory](../src/AgenticLab.Web/Learning/HostingStory.cs) own illustrative content and reveal
state. Keep source-review dates aligned with the curated hosting list when rechecking it.

Tests cover stage URLs, navigation, reveal/reset behavior and topic references:

```sh
dotnet test tests/AgenticLab.Web.Tests/AgenticLab.Web.Tests.csproj
```

For backend-free desktop interaction and screenshot checks, run
`node tools/web-smoke.mjs --learn-only` against the standalone Web URL using the temporary
Playwright setup in [tools/README.md](../tools/README.md). This checks keyboard reveals, cumulative
nodes, optional configurations and all six synthetic loop outcomes without model or tool calls.
