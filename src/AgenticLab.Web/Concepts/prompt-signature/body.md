## What is a prompt signature?

Every time an agent calls the model it sends a whole **request** — not just the latest user
message, but the system prompt, the tool catalogue, the conversation so far and any tool
results. The **prompt signature** is a breakdown of that request by **category**, so you can
see *what* fills the model's context and *how big* each part is.

## The categories

A request is assembled from a few kinds of content:

- **System** — the system/harness prompt and any always-on instructions.
- **User** — the messages the person sent.
- **Assistant** — the model's own earlier replies and tool calls.
- **Tool result** — the data tools returned and fed back into the context.
- **Tools catalogue** — the schemas of the tools the model is allowed to call.

Measuring each category (here, in characters) shows where the budget goes. A long system
prompt or a large tool catalogue can dominate a request before the user has said much at all.

## Characters and tokens

A model does not read characters. It reads **tokens**: pieces of text that are often a short
word or part of a longer one. Token counts are what a provider bills and what the context
window limits. Providers report them per request as two numbers: **input** tokens for
everything sent, and **output** tokens for what the model generated. They do not report how
many tokens each category used, which is why the categories here are measured in characters.

## Why compare requests

Across a multi-step run the agent calls the model repeatedly, and most of each request is the
**same** as the last one — the system prompt and tool catalogue don't change, and earlier
turns are re-sent verbatim. Providers reward that stability with **prompt caching**: the
unchanged **prefix** of a request can be served from cache, which is cheaper and faster.

Comparing the current request against the previous one — and measuring how much of the prefix
is byte-identical — gives a **stability / match** score. A high match means the prompt is
cache-friendly; a low match means something near the front of the prompt changed and broke
the cache.

## In this application (Agentic Lab)

Turning on **Prompt signature** (a display option) shows a panel under the
flow diagram. It reads the **real captured requests** the harness sent to the model and draws a
stacked bar for the **Current** request — one segment per category, sized by its share of
characters — plus a **Previous** bar once a second request has been sent. A **Match** bar shows
the share of the current request that is identical to the previous one (its reused prefix). The
five category colours are fixed (they encode message roles, not the vendor brand), so they stay
the same as you switch vendors.

Next to each bar's character total, the panel shows the **tokens the provider reported**, as
input and output. For a finished exchange this is the sum of all its model calls, the same total
as on the reply in the conversation. An agent that uses tools calls the model several times, and
every call sends the context so far again, so the input sum counts that context once per call.
To see one call alone, select a stage of that model turn in the Execution panel. Input counts are
for whole requests, so they also cover the tool catalogue and message framing that the character
total leaves out. Tokens are missing when the provider reports no usage; the panel never estimates
them from the character count.

When the provider reports it, the input count also shows how many of those tokens were **cached**:
read from the provider's prompt cache instead of processed again. Match is calculated from the
captured text; the cached count is what the provider measured. They can differ. Providers cache
only prompts above a minimum size (1,024 tokens for OpenAI models), so a short prompt can have a
high match and still show `0 cached`.
