# AI Worklog

This log is for Alex's AI-assisted development sessions. Keep entries short and factual.

## Template

```md
## YYYY-MM-DD - Short task title

- Goal:
- Changed:
- Verified:
- Risks:
- Follow-ups:
```

## 2026-07-04 - Personal AI framework bootstrap

- Goal: Create Alex's local AI context layer for GoodCopBadCopUnity.
- Changed: Added root `AGENTS.md`, Alex docs under `Docs/AI/Alex`, and snapshot tooling under `Tools/AI`.
- Verified: Ran `Tools/AI/Refresh-AlexAIContext.ps1`; generated `Docs/AI/Alex/generated/PROJECT_SNAPSHOT.md` and `.json`.
- Risks: A font asset was noted as dirty during bootstrap; always trust current `git status --short` over historical worklog state.
- Follow-ups: Consider Unity Editor validation scripts after several real AI sessions reveal repeated failure modes.

## 2026-07-04 - Deep project context survey

- Goal: Document the actual runtime systems needed for faster future AI-assisted work.
- Changed: Expanded `PROJECT_MAP.md`; added `SYSTEMS.md`, `GAMEPLAY_FLOWS.md`, `NETCODE_NOTES.md`, `DATA_AND_CONTENT.md`, and `TASK_RECIPES.md`; updated risks, memory, and decisions.
- Verified: Static source survey of campaign, shift, suspect, player, interaction, networking, UI, dialogue, save, shop, and threat systems; reran `Tools/AI/Refresh-AlexAIContext.ps1`.
- Risks: Unity Editor was not launched for this survey, so scene wiring and serialized references still need Unity/MCP validation before behavior changes.
- Follow-ups: Add targeted Unity validation scripts after the next real gameplay/networking task reveals repeat checks.
