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

## 2026-07-05 - Feature architecture decision

- Goal: Record Alex's target architecture for future significant feature work before implementing packages or code changes.
- Changed: Added `FEATURE_ARCHITECTURE.md`; linked it from `README.md`; recorded the model/service/reactive decision in `DECISIONS.md`; added DOTween guidance for simple animations.
- Verified: Documentation-only change; no Unity assets, packages, or project settings intentionally modified.
- Risks: R3, UniTask, DOTween, and DI are architectural choices only at this point; package installation and compile validation still need a separate implementation task.
- Follow-ups: When Alex approves implementation, add packages and create the first feature module using this pattern.

## 2026-07-05 - Install architecture dependencies

- Goal: Install the selected architecture dependencies and update Alex's docs.
- Changed: Added OpenUPM scoped registry plus `com.cysharp.r3`, `com.cysharp.unitask`, and `jp.hadashikick.vcontainer`; documented VContainer as the selected DI container; recorded that DOTween/DOTween Pro already exists under `Assets/Plugins/Demigiant`.
- Verified: Manifest and lock JSON parsed successfully; package versions were checked against OpenUPM registry metadata.
- Risks: Unity Editor was not launched to avoid touching unrelated dirty asset/meta changes, so Unity compile/package resolution should still be checked in the next editor session.
- Follow-ups: In the first new feature module, add VContainer registration/lifetime scope code and verify R3/UniTask compile in Unity.

## 2026-07-05 - Environment system architecture migration

- Goal: Move fog/environment switching from legacy scene-array logic into the new model/service/adapter architecture.
- Changed: Added EnvironmentPreset, EnvironmentSchedule, EnvironmentModel, EnvironmentService, campaign/render adapters, and MainSceneLifetimeScope registrations; added an Environment Schedule asset and wired it in Main.unity; disabled BackgroundManager's automatic first-slot apply behavior.
- Verified: Ran static searches for old Environment type usage and `git diff --check`; no Unity compile check was available from generated project files.
- Risks: Unity Editor compile/console validation is still needed; day 3/4/6 final color presets still need authored assets and visual QA.
- Follow-ups: Verify in Unity, then replace placeholder schedule slots with authored orange-red, yellow, and ashy-gray presets.

## 2026-07-05 - Odin inspector tooling standard

- Goal: Record Odin Inspector as the preferred tool for internal inspector/debug interfaces.
- Changed: Documented Odin in `FEATURE_ARCHITECTURE.md` as an editor/tooling dependency for debug buttons, read-only runtime state, and ScriptableObject authoring helpers.
- Verified: Static documentation update only; Odin asset import is present under `Assets/Plugins/Sirenix`.
- Risks: Unity Editor compile/console validation is still needed after the Odin import.
- Follow-ups: Use editor-only `OdinEditorWindow` tools under `Tools/_GoodCopBadCop/` for debug workflows.

## 2026-07-05 - Settings persistent display mode

- Goal: Wire Display Mode using the new persistent reactive settings architecture.
- Changed: Added SettingsModel with PersistentReactiveProperty-backed DisplayMode, SettingsService commands, SettingsApplier Unity bridge, and SettingsMenuPresenter/View bindings for the Display Mode dropdown.
- Verified: Ran git diff --check; searched for misplaced Unity/PlayerPrefs calls; Unity Editor log tail showed no fresh settings compile errors.
- Risks: Only Display Mode is wired; other settings remain UI-only.
