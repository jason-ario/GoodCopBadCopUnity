# Codex Project Context

Codex-specific project index and generated context for GoodCopBadCopUnity.

This folder is not shared AI policy. It exists so Codex can quickly orient inside the project without forcing the same index on Bezi or other tools.

## Context Rule

Do not read every file in this folder by default. Use this file as the router, then open only the docs needed for the current task.

## Task Routing

| Task type | Read next |
| --- | --- |
| Existing gameplay bug | `PROJECT_MAP.md`, then `SYSTEMS.md` or `GAMEPLAY_FLOWS.md` when needed |
| Netcode/multiplayer work | `NETCODE_NOTES.md`, relevant source files |
| Content, prefab, ScriptableObject, scene work | `DATA_AND_CONTENT.md`, `KNOWN_RISKS.md` |
| Repeatable task pattern | `TASK_RECIPES.md` |
| Broad project orientation | `PROJECT_MAP.md`, then one focused deep doc |
| Generated project facts | `generated/PROJECT_SNAPSHOT.md` |

## File Index

- `PROJECT_MAP.md` - project map and important Unity facts.
- `SYSTEMS.md` - subsystem ownership and main source files.
- `GAMEPLAY_FLOWS.md` - runtime flows for campaign, shifts, suspects, UI, and multiplayer-sensitive sequences.
- `NETCODE_NOTES.md` - Netcode authority notes and multiplayer risk checks.
- `DATA_AND_CONTENT.md` - content, prefab, ScriptableObject, and scene authoring map.
- `TASK_RECIPES.md` - task-specific project lookup recipes.
- `KNOWN_RISKS.md` - project risks to check before risky edits.
- `generated/PROJECT_SNAPSHOT.md` - generated human-readable project snapshot.
- `generated/PROJECT_SNAPSHOT.json` - generated machine-readable project snapshot.
## Refresh Policy

Refresh `generated/PROJECT_SNAPSHOT.*` before broad project analysis, after package/scene/script-folder/build-settings changes, or when generated context looks stale.

From the repo root:

```powershell
.\Tools\AI\Refresh-CodexAIContext.ps1 -ProjectRoot "D:\projects\GoodCopBadCopUnity"
```

The refresh script writes only to `Docs/AI/Codex/generated`.

## Memory Notes

- v1 memory is Markdown plus generated snapshots; no semantic DB/vector index is part of this setup.
- Unity validation scripts are intentionally deferred for now; use manual/MCP verification until repeated checks are worth automating.
- Durable architecture/process rules belong in shared `Docs/AI` files, not in this Codex index.
