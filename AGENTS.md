# GoodCopBadCopUnity Agent Guidance

## Project Overview

- This is a Unity project. Main product content lives under `Assets/_GoodCopBadCop`.
- Treat other top-level `Assets/*` folders as vendor, imported package, demo, or support content unless the task explicitly targets them.
- Bezi Sidekick may be installed in this repo, but these instructions do not require Bezi and should not change Bezi users' workflow.

## Project AI Context

For Codex project coding tasks:

- Read `Docs/AI/README.md`.
- Read `Docs/AI/SESSION_START.md`.
- Do not read every file in `Docs/AI` by default.
- If deeper project context is needed, read `Docs/AI/Codex/README.md` and use its routing table.
- Inspect actual source files before making code changes.

## Working Pattern

- For each task, identify: goal, relevant context, constraints, and done-when criteria.
- Before editing, run `git status --short` and preserve unrelated user changes.
- Keep changes scoped to the requested behavior and the smallest relevant subsystem.
- Prefer existing Unity/project patterns over new abstractions.

## Unity Safety

- Do not edit `Library`, `Temp`, `Obj`, `Logs`, `UserSettings`, or generated Unity cache folders.
- Do not modify imported/vendor assets unless the user explicitly asks.
- Be careful with scene, prefab, `.asset`, and `.meta` files; they can carry large serialized changes.
- For Netcode changes, check ownership, host/client/server paths, RPC direction, and late-join behavior.

## Verification

- After script changes, verify Unity compilation when Unity/MCP tools are available.
- Check the Unity console for new errors or warnings after meaningful Unity edits.
- For visual, scene, prefab, or UI changes, use screenshots/play checks when practical.
- If verification cannot be run, say exactly what was not run and why.
