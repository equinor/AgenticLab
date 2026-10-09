# Sample Workspace

Use this folder as an example workspace for Agentic Lab's coding agent hosts (harnesses),
such as the **GitHub Copilot** example. It includes sample agents and skills.

1. [Run Agentic Lab locally](../README.md#run-locally).
2. In Live Flow, select **GitHub Copilot** and **Coder**.
3. Open **Settings** and set **Workspace** to this folder's absolute path on the AiService machine
   (for example, `C:\code\agenticlab\sample-workspace`).
4. Try: "List the files in this workspace and explain the available sample agents and skills."

This uses your configured model, not the GitHub Copilot service. Use only trusted files;
workspace tools are [not sandboxed](../SECURITY.md). See the [workspace guide](../docs/workspace.md) for more.

## Contents

- `src/todo.py` and `tests/test_todo.py`: a tiny example project with a deliberate bug to find.
- `agents/`: Guide (read-only), Codex and Scaffolder (write files and run commands).
- `skills/`: `review-code` (read-only), and `get-date` and `whoami`, which need the terminal.
- `instructions/`: an optional response-style instruction.

## Read-only sample mode

Shared deployments such as Radix run with `Workspace:Mode=ReadOnlySample`: every user works on this
folder, bundled in the image, without choosing a path. Only read-only tools are offered, so only
Ask, Plan, Guide and the `review-code` skill are available. Try Guide with: "Use the review-code skill
to review src/todo.py." See [read-only sample mode](../docs/workspace.md#read-only-sample-mode).