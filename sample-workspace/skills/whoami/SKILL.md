---
name: whoami
description: Get the username of the account the agent runs as, on Windows, macOS or Linux, using the terminal.
allowed-tools: RunCommand
---

# Get the current username (Windows, macOS, Linux)

Use this skill when the user asks who they are, what their username is, or which account is currently
running the agent.

## Steps

1. Call the `RunCommand` tool with `command` set to `whoami` and `arguments` left empty. This is the same
   on every operating system:

   ```
   whoami
   ```

   - On Windows it returns `DOMAIN\username` (or `MACHINE\username` for a local account).
   - On macOS and Linux it returns just the username.

2. Report the value from the command's output back to the user. If the user only wants the username,
   you may drop the `DOMAIN\` prefix from the output yourself. Do not invent or guess the username —
   only state what the command returned.

## Notes

- `RunCommand` accepts only exact allowlisted command lines. `whoami` must be run with no arguments:
  `whoami /all`, `whoami /user`, `powershell`, `$env:USERNAME`, `echo $USER` and `id` are all rejected.
- The result is the account the agent service runs under, which may differ from the person chatting.
