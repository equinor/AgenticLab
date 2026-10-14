---
name: review-code
description: Review code in the workspace and report bugs, risks and improvements, without changing any files.
allowed-tools: ReadFile, ListFiles
---

# Review code (read-only)

Use this skill when the user asks you to review, check or explain the quality of code in the
workspace. You only read files; never suggest that you have changed anything.

## Steps

1. Use `ListFiles` to find the files the user means. If it's unclear which code to review, ask, or
   start with the main source folder (for example `src/`).
2. Use `ReadFile` to read each file you review, and any tests that cover it.
3. Look for, in this order:
   - **Bugs:** wrong results, off-by-one errors, unhandled empty or invalid input.
   - **Risks:** missing validation, surprising side effects, behavior the tests don't cover.
   - **Readability:** unclear names, duplicated logic, missing explanations for non-obvious code.
4. Check each finding against the code you actually read. Don't report something you haven't seen.

## Report format

Start with a one-sentence summary. Then list each finding as:

- **Severity** (bug, risk or suggestion), **file and function**, what happens, and why.
- A short suggested fix, described in words or as a small code snippet for the user to apply.

End with what looks good, so the review is balanced. Keep the whole report short.
