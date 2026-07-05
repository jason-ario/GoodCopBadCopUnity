# Alex AI Workspace

This folder is Alex's personal AI context layer inside the shared GoodCopBadCopUnity project. It is meant for Codex/MCP sessions and should not impose workflow rules on other contributors or Bezi users.

## Context Rule

Do not load every file in this folder by default. Use this file as the router, then open the smallest set of deeper docs needed for the current task.

Default start for Alex tasks:

1. Read `AGENTS.md`.
2. Read this `README.md`.
3. Read `SESSION_START.md`.
4. Pick task-specific docs from the routing table below.
5. Inspect real project files before making claims or edits.

Avoid reading `generated/PROJECT_SNAPSHOT.json` directly unless a script/tool needs machine-readable data. Prefer `generated/PROJECT_SNAPSHOT.md` for human context.

## Task Routing

| Task type | Read next |
| --- | --- |
| Quick small edit | `SESSION_START.md`, `WORKFLOW.md`, target source files |
| New significant feature | `SESSION_START.md`, `FEATURE_ARCHITECTURE.md`, `DECISIONS.md`, target source files |
| Existing gameplay bug | `PROJECT_MAP.md`, then one of `SYSTEMS.md` or `GAMEPLAY_FLOWS.md` |
| Netcode/multiplayer work | `NETCODE_NOTES.md`, relevant flow/system section |
| Content, prefab, ScriptableObject, scene work | `DATA_AND_CONTENT.md`, `KNOWN_RISKS.md` |
| Tests or base infrastructure | `WORKFLOW.md` testing section, `FEATURE_ARCHITECTURE.md` if architectural |
| Broad project orientation | `PROJECT_MAP.md`, then one focused deep doc |
| Repeated task pattern | `TASK_RECIPES.md` |
| Memory/process/doc maintenance | `MEMORY.md`, `WORKLOG.md`, `DECISIONS.md` |

## File Index

- `SESSION_START.md` - compact context packet for new Codex sessions.
- `PROJECT_MAP.md` - current project map and important Unity facts.
- `SYSTEMS.md` - deeper subsystem ownership map for gameplay, player, UI, data, and threats.
- `GAMEPLAY_FLOWS.md` - end-to-end runtime flows agents should understand before feature work.
- `NETCODE_NOTES.md` - local Netcode patterns, authority rules, and multiplayer risk checks.
- `DATA_AND_CONTENT.md` - source-of-truth content folders and ScriptableObject authoring map.
- `FEATURE_ARCHITECTURE.md` - Alex's target model/service/reactive architecture for new significant features.
- `TASK_RECIPES.md` - quick working recipes for common future AI tasks.
- `WORKFLOW.md` - Alex's preferred Codex/MCP work cycle, testing rules, and commit naming.
- `MEMORY.md` - how local AI memory is organized and kept context-efficient.
- `WORKLOG.md` - running log of AI-assisted sessions.
- `DECISIONS.md` - durable decisions and conventions.
- `KNOWN_RISKS.md` - project risks agents should check before changing code or assets.
- `generated/PROJECT_SNAPSHOT.md` - generated human-readable project snapshot.
- `generated/PROJECT_SNAPSHOT.json` - generated machine-readable snapshot.

## Operating Rules

- Treat `AGENTS.md` as the repo entrypoint and this folder as Alex-specific context.
- Prefer this folder over external memory for project facts.
- Read broad docs on demand, not preemptively.
- Update `WORKLOG.md` after substantial AI-assisted work.
- Refresh `generated/PROJECT_SNAPSHOT.*` before large tasks or when project structure changes.
- Keep personal process notes here, not in root `AGENTS.md`, unless the rule should apply to everyone.

## Refresh Command

From the repo root:

```powershell
.\Tools\AI\Refresh-AlexAIContext.ps1 -ProjectRoot "D:\projects\GoodCopBadCopUnity"
```

The refresh script writes only to `Docs/AI/Alex/generated`.