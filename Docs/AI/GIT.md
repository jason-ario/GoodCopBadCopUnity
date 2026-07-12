# Git Workflow

Shared rules for AI-assisted git work in this Unity repo.

## When To Read This

Read this file before any Git-related task.

## Before Staging Or Committing

- Run `git status --short`.
- Review unstaged and staged changes.
- Do not stage unrelated user changes.
- If unrelated changes exist, stage only the files or hunks that belong to the requested task.
- If the intended commit scope is unclear, ask before staging.
- Keep vendor/plugin imports in separate commits.
- Avoid mixing gameplay code and content changes unless they are one coherent task.

## Commit Naming

Use a lightweight Conventional Commits style:

```text
type(scope): short imperative summary
```

Examples:

```text
feat(settings): add persistent reactive property
fix(prefab): restore time card machine setup
refactor(environment): split model and render adapter
docs(ai): document feature architecture
chore(packages): add VContainer and R3
content(environment): add fire skybox presets
editor(environment): add debug window
build(voice): import Dissonance package
```

Common types:

- `feat` - new gameplay or user-facing feature.
- `fix` - bug fix.
- `refactor` - structure change without intended behavior change.
- `content` - scenes, prefabs, materials, ScriptableObjects, visual/audio presets.
- `editor` - Unity editor tooling, Odin windows, custom inspectors.
- `docs` - documentation.
- `chore` - housekeeping, generated files, cleanup.
- `build` - packages, dependencies, vendor/plugin imports.
- `test` - tests.

Rules:

- Write commit messages in English.
- Prefer imperative verbs: `add`, `fix`, `split`, `document`, `import`.
- Avoid vague messages such as `Update stuff`, `ADD -`, or `FIX -`.
