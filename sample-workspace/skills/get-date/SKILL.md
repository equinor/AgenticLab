---
name: get-date
description: Get the current date and time on Windows, macOS or Linux using the terminal.
allowed-tools: RunCommand
---

# Get the current date and time (Windows, macOS, Linux)

Use this skill when the user asks for the current date, the current time, or both.

## Steps

1. Check the operating system in the `<environment>` section of your instructions, then call the
   `RunCommand` tool with the matching exact command:

   | Operating system | Date | Time |
   |------------------|------|------|
   | Windows | `command`: `date`, `arguments`: `/t` | `command`: `time`, `arguments`: `/t` |
   | macOS / Linux | `command`: `date`, no arguments | (same call — `date` returns both) |

   - On Windows, run `date /t` for the date and `time /t` for the time, as two separate calls when the
     user wants both. The output follows the system's regional format, shows hours and minutes only,
     and has no time zone.
   - On macOS and Linux, a single `date` call returns the date, the time (with seconds) and the time
     zone.

2. If the user wants a specific format (for example ISO `yyyy-MM-dd` or 24-hour time), reformat the value
   from the command's output yourself. Do not run another command to format it.

3. Report the value from the command's output back to the user. Do not invent or guess the date or
   time, or add seconds or a time zone the output did not include — only state what the command returned.

## Notes

- `RunCommand` accepts only exact allowlisted command lines. Anything else is rejected, including
  `powershell Get-Date`, `date +%F`, `date -u` and a bare `date` or `time` on Windows (which would
  try to change the system clock).
