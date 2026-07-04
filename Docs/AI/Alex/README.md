# Alex AI Workspace

This folder is Alex's personal AI context layer inside the shared GoodCopBadCopUnity project. It is meant for Codex/MCP sessions and should not impose workflow rules on other contributors or Bezi users.

## Start Here

- `PROJECT_MAP.md` - current project map and important Unity facts.
- `WORKFLOW.md` - Alex's preferred Codex/MCP work cycle.
- `MEMORY.md` - how local AI memory is organized.
- `WORKLOG.md` - running log of AI-assisted sessions.
- `DECISIONS.md` - durable decisions and conventions.
- `KNOWN_RISKS.md` - project risks agents should check before changing code or assets.
- `generated/PROJECT_SNAPSHOT.md` - generated project snapshot.
- `generated/PROJECT_SNAPSHOT.json` - machine-readable snapshot.

## Operating Rules

- Treat `AGENTS.md` as the repo entrypoint and this folder as Alex-specific context.
- Prefer this folder over external memory for project facts.
- Update `WORKLOG.md` after substantial AI-assisted work.
- Refresh `generated/PROJECT_SNAPSHOT.*` before large tasks or when project structure changes.
- Keep personal process notes here, not in root `AGENTS.md`, unless the rule should apply to everyone.

## Refresh Command

From the repo root:

```powershell
.\Tools\AI\Refresh-AlexAIContext.ps1 -ProjectRoot "D:\projects\GoodCopBadCopUnity"
```

The refresh script writes only to `Docs/AI/Alex/generated`.
