# Decisions

## 2026-07-04 - Personal AI docs live under `Docs/AI/Alex`

- Decision: Alex's AI context belongs in `Docs/AI/Alex`, not as broad team documentation.
- Reason: The project is shared and Bezi users should not inherit Alex-specific process rules.
- Consequence: Root `AGENTS.md` stays short and points agents to Alex's docs only for Alex tasks.

## 2026-07-04 - Use hybrid snapshots, not semantic DB

- Decision: v1 memory is Markdown plus generated snapshots.
- Reason: This is ready immediately and has low maintenance cost.
- Consequence: No vector database, embedding pipeline, or separate indexing service in v1.

## 2026-07-04 - Unity validation scripts are deferred

- Decision: Do not add Unity Editor validation scripts in v1.
- Reason: We need a few real AI sessions first to identify the most valuable checks.
- Consequence: Use manual/MCP verification for now; revisit validators later.

## 2026-07-04 - Bezi remains optional

- Decision: Do not build this framework around Bezi.
- Reason: Alex wants a local-first Codex/MCP workflow that does not affect other contributors.
- Consequence: Bezi can stay installed, but root instructions must not require it.

## 2026-07-04 - Deep context is split by task type

- Decision: Keep `PROJECT_MAP.md` as the index/overview and put deep details in `SYSTEMS.md`, `GAMEPLAY_FLOWS.md`, `NETCODE_NOTES.md`, `DATA_AND_CONTENT.md`, and `TASK_RECIPES.md`.
- Reason: One huge project map would waste context for focused future sessions.
- Consequence: New Codex threads should read only the deep file that matches the current task after reading the overview.
