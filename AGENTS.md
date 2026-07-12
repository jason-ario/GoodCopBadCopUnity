# GoodCopBadCopUnity Agent Guidance

## Project Overview

- This is a Unity project. Main product content lives under `Assets/_GoodCopBadCop`.
- Treat other top-level `Assets/*` folders as vendor, imported package, demo, or support content unless the task explicitly targets them.
- Bezi Sidekick may be installed in this repo, but these instructions do not require Bezi and should not change Bezi users' workflow.

## Project AI Context

For AI-assisted project coding tasks:

- Read `Docs/AI/SESSION_START.md` for compact shared project context.
- For architecture-sensitive work, read `Docs/AI/ARCHITECTURE.md`.
- For process, tests, or verification, read `Docs/AI/WORKFLOW.md`.
- For any Git-related work, read `Docs/AI/GIT.md` before running commands that modify repository state.
- Do not read every file in `Docs/AI` by default.
- Inspect actual source files before making code changes.
- Tool-specific context may exist under `Docs/AI/<ToolName>`; use it only when it matches the current tool/task.

## Working Pattern

- For each task, identify: goal, relevant context, constraints, and done-when criteria.
- Before editing, run `git status --short` and preserve unrelated user changes.
- Keep changes scoped to the requested behavior and the smallest relevant subsystem.
- Prefer existing Unity/project patterns over new abstractions.

## Unity Safety

- Do not edit `Library`, `Temp`, `Obj`, `Logs`, `UserSettings`, or generated Unity cache folders.
- Do not modify imported/vendor assets unless the user explicitly asks.
- Be careful with scene, prefab, `.asset`, and `.meta` files; they can carry large serialized changes.
- Do not create or hand-edit Unity `.meta` files manually. Let Unity generate/import them, and use Unity/AssetDatabase tooling when metadata must change.
- For Netcode changes, check ownership, host/client/server paths, RPC direction, and late-join behavior.

## Verification

- After script changes, verify Unity compilation when Unity/MCP tools are available.
- Check the Unity console for new errors or warnings after meaningful Unity edits.
- For visual, scene, prefab, or UI changes, use screenshots/play checks when practical.
- If verification cannot be run, say exactly what was not run and why.
