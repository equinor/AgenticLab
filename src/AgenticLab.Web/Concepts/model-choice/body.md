## Why the model matters

The harness, tools and instructions can stay exactly the same while the model changes. The model
still decides what happens next: answer now, request a tool, request several tools, or stop. Those
decisions vary a lot between models, so the same agent can be faster, cheaper or more accurate on
one model than on another.

## Why models differ in tool calling

Tool calling is a learned skill. A model sees each tool's name, description and argument schema in
its context and has to produce a structured request that matches. Models trained more heavily on
tool use tend to:

- **Decide better whether a tool is needed.** A weaker model may answer from memory when it should
  look something up, or call a tool for something it already knows.
- **Pick the right tool and fill in valid arguments.** Misread descriptions or malformed arguments
  cost a failed call and another round-trip.
- **Batch independent calls.** Some models request several calls in one response (parallel calls);
  others request one, wait for the result, then request the next.
- **Plan the steps.** A model that breaks the task down well needs fewer calls. A weaker one may
  repeat a call, loop on a failing tool or stop before it has what it needs.
- **Use the results.** Reading long tool output and combining it correctly is part of the skill too.

Reasoning models add another trade-off: they spend extra output tokens thinking before they act.
Each response can be slower, but a better plan can mean fewer round-trips overall.

## Why the number of round-trips matters

Every round-trip sends the whole context again: instructions, tool descriptions, the conversation
and all earlier tool results. Each extra call therefore costs more than the one before it, adds
waiting time and adds another chance to go wrong. Fewer, better-chosen calls usually means a
faster, cheaper and more reliable agent.

## Comparing models fairly

- Use the same task, tools and instructions, and start each attempt from the same history.
- Look at the answer first: a fast run with a wrong answer is not efficient.
- Then compare round-trips, tool calls, input and output tokens, and elapsed time.
- Repeat the task. Model output varies between runs, so one attempt is an example, not a result.

A model choice changes behaviour, cost and quality. It does not change permissions: whichever model
is used, the harness still decides which tool requests run.

## In this application (Agentic Lab)

The **Model** selector above the conversation lists the models the service allows; the server
rejects any other. **GPT-5.6 Luna** is the default. **GPT-4o** is an older model to compare against:
newer models tend to check more sources and answer more precisely, which means more tool calls,
tokens and time. Try *"Compare Alan Turing and Ada Lovelace using Wikipedia: where was each born
and what is each best known for?"* with **WikiAssistant** on both. GPT-4o answers from a few
lookups; GPT-5.6 Luna usually reads more pages, such as the district Turing was born in, and
gives a more exact answer.
Your choice applies to every following message, including questions the **Orchestrator** delegates
to A2A specialists, and is remembered in this browser.

Each answer is labelled with the model that produced it and a line such as
`22.3 s · model 19.1 s · 9,847 tokens · 14 tool calls`:

- **Total time**: from sending the message to the final answer, including tools.
- **model**: time spent waiting for the model. Tool time and manual stepping pauses are excluded.
- **tokens**: input plus output. Input grows each turn because the whole context is sent again.
- **tool calls**: how many tools the model asked the harness to run.
- **turns**: LLM round-trips. The loop badge on the **Model** node shows them live.

For a fair comparison, start a **New conversation** before each attempt, because switching model
keeps the history and the next model can reuse earlier answers. Use **Auto** mode so total time
excludes pauses while you step manually.

The same figures are recorded in telemetry by agent and model. Security Lab agents replay a scripted
model, so the selector does not apply to them.
