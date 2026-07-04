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
- Risks: Existing dirty asset `Assets/_GoodCopBadCop/_Fonts/My_handwriting SDF.asset` predates this work and should not be overwritten.
- Follow-ups: Consider Unity Editor validation scripts after several real AI sessions reveal repeated failure modes.
