# AI Session Start

Compact context packet for new AI/MCP sessions. Keep it short and open deeper docs only when the current task needs them.

## Read Strategy

- Start from `AGENTS.md`, `Docs/AI/README.md`, then this file.
- Run `git status --short` before edits.
- Do not read all AI docs by default.
- Prefer direct source inspection over stale memory when behavior matters.

## Project Snapshot

- Unity project: `GoodCopBadCopUnity`.
- Unity version from project settings: `6000.5.1f1`.
- Main product root: `Assets/_GoodCopBadCop`.
- Main scene: `Assets/_GoodCopBadCop/_Scenes/Main.unity`.
- Main script root: `Assets/_GoodCopBadCop/_Scripts`.
- Main data root: `Assets/_GoodCopBadCop/_Data`.
- Main prefab root: `Assets/_GoodCopBadCop/_Prefabs`.
- Treat other top-level `Assets/*` folders as vendor/imported/support unless the task explicitly targets them.

## Current Architecture Direction

For new significant features, prefer:

```text
Model = observable state.
Service = commands, rules, validation, and mutations.
Presenter/View/Adapter = Unity, UI, input, animation, Netcode, and legacy bridge.
Config/Data = ScriptableObject or authored content, not runtime state.
```

Selected tools:

- VContainer for DI and feature registration.
- R3 for reactive state/events.
- UniTask for async flows.
- DOTween for simple tween animations.
- Odin Inspector for internal editor/debug tooling.
- Unity Test Framework with NUnit for tests.

Do not mass-refactor legacy systems into the new shape. Apply this to new modules first, or to old code only when the current task already requires touching that area.

## Code Shape

- Use `GoodCopBadCop.*` namespaces for new feature/framework code.
- Use block-style namespaces, not file-scoped namespaces, because this Unity project currently rejects C# 9 file-scoped syntax.
- Prefer public concrete model/service classes plus public interfaces when the service is the only intended consumer of mutable model members through DI.
- Register feature dependencies in `MainSceneLifetimeScope` using the current VContainer style.

## Existing Project Reality

- The current project is MonoBehaviour-heavy and singleton-heavy.
- Gameplay systems often communicate through scene references, static events, singletons, NetworkVariables, ServerRpcs/ClientRpcs, and Timeline callbacks.
- Multiplayer impact is common; assume Netcode risk until proven otherwise.
- Scene/prefab/asset diffs can be large and should be inspected carefully.

## Verification Defaults

- After script changes, verify Unity compilation and read Console errors when MCP/Unity tools are available.
- For Netcode changes, check host, client, server ownership, RPC direction, and late-join behavior.
- For UI/visual/scene/prefab work, use screenshots, play checks, or Unity inspection when practical.
- For docs-only work, inspect diffs and keep docs scoped.
