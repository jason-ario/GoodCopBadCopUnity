# Local AI Memory

## Memory Model

This project uses hybrid local memory:

- Stable hand-written context in `Docs/AI/Alex/*.md`.
- Generated project facts in `Docs/AI/Alex/generated/PROJECT_SNAPSHOT.*`.
- Session history in `WORKLOG.md`.
- Durable choices in `DECISIONS.md`.

No semantic database or vector index is part of v1.

## Context Budget Rules

- Keep `AGENTS.md`, `README.md`, and `SESSION_START.md` short enough to load at the start of most Alex tasks.
- Treat deep docs as demand-loaded references, not automatic context.
- Add `When To Read` and short summary sections to large docs instead of expecting agents to read the entire file.
- Prefer focused source inspection over copying large docs into the prompt.
- Use `generated/PROJECT_SNAPSHOT.md` for quick human project facts; reserve `generated/PROJECT_SNAPSHOT.json` for scripts and tools.
- If a doc grows beyond quick scan size, split by task area or add a smaller routing section at the top.

## Stable Facts

Store high-level facts in `PROJECT_MAP.md` or `DECISIONS.md`:

- Project layout and source-of-truth folders.
- Architecture rules that Alex wants agents to follow.
- Known gameplay system ownership and coupling.
- Decisions that should survive across sessions.

Store deeper facts in focused files:

- `SYSTEMS.md` for subsystem ownership and source files.
- `GAMEPLAY_FLOWS.md` for runtime sequences.
- `NETCODE_NOTES.md` for multiplayer authority and verification rules.
- `DATA_AND_CONTENT.md` for ScriptableObject, prefab, scene, and content authoring maps.
- `TASK_RECIPES.md` for repeatable Codex/MCP task checklists.

## Generated Facts

Store these only in `generated/PROJECT_SNAPSHOT.*`:

- File counts.
- Package versions.
- Scene lists.
- Script folder counts.
- Current git status.
- Generated warnings.

Regenerate snapshots instead of hand-editing them.

## Worklog Entries

Use `WORKLOG.md` for:

- What task was done.
- Important files touched.
- Checks run.
- Risks or follow-ups.
- Any project facts discovered that should later move into `PROJECT_MAP.md` or `DECISIONS.md`.

## Refresh Policy

Refresh the snapshot:

- Before large feature work.
- After package, scene, script-folder, or build-settings changes.
- Before handing a new Codex thread a broad project task.
- When the generated context looks stale.

Do not rely on generated memory for rules that must always apply; put those in `AGENTS.md` or stable Alex docs.
