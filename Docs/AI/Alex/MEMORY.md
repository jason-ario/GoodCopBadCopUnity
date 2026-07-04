# Local AI Memory

## Memory Model

This project uses hybrid local memory:

- Stable hand-written context in `Docs/AI/Alex/*.md`.
- Generated project facts in `Docs/AI/Alex/generated/PROJECT_SNAPSHOT.*`.
- Session history in `WORKLOG.md`.
- Durable choices in `DECISIONS.md`.

No semantic database or vector index is part of v1.

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
