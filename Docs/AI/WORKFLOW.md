# AI Workflow

## Default Task Loop

1. Identify the goal, relevant context, constraints, and done-when criteria.
2. Run `git status --short` before edits.
3. Read the smallest useful set of files, scenes, prefabs, or generated snapshots.
4. Make scoped changes.
5. Verify with the most relevant checks.
6. Summarize changes and call out anything not verified.

## Unity MCP Loop

When Unity MCP tools are available:

1. Read editor state before doing Unity work.
2. Inspect project/scene resources before mutating GameObjects, prefabs, or components.
3. Prefer object IDs or exact asset paths over loose names.
4. After script edits, wait for compilation to finish.
5. Read console errors/warnings.
6. For scene, UI, camera, or visual work, capture screenshots or run a play check when practical.

## Before Editing

- Check whether the target is product code under `Assets/_GoodCopBadCop` or vendor/imported code.
- Check whether the change touches Netcode, serialized assets, scene files, prefabs, or `.meta` files.
- If a file has unrelated user changes, preserve them and work around them.

## Verification Defaults

- C# logic change: Unity compile check and console check when available.
- Netcode change: inspect server/client paths, ownership, host behavior, late join behavior, and RPC direction.
- UI or visual change: screenshot or Play Mode check when available.
- Data/prefab/scene change: inspect diff carefully and verify references in Unity when available.
- Docs/tooling-only change: run the affected script and inspect generated output.

## Testing Rules

Use Unity Test Framework with NUnit for project tests.

Current test locations:

- EditMode tests: `Assets/_GoodCopBadCop/_Scripts/Editor/Tests`.
- PlayMode tests: create `Assets/_GoodCopBadCop/_Tests/PlayMode` with a dedicated test asmdef when the first real PlayMode test is needed.
- Do not put project tests in vendor/imported asset folders.

Default choice:

- Use EditMode `NUnit.Framework` tests for pure C# behavior, models, services, storage, persistence, small adapters, and architecture/framework utilities.
- Use `[Test]` for synchronous tests.
- Use `[UnityTest]` only when the test needs frames, coroutines, scene lifecycle, or Unity runtime waiting.
- Use PlayMode tests only when Unity scene lifecycle, physics, UI interaction, Netcode behavior, or runtime object wiring is the point of the test.

Write focused tests for important base components and reusable infrastructure. Prioritize tests when a component is:

- Used by multiple features or systems.
- Part of the AI/framework layer, DI setup, persistence, storage, or architecture glue.
- Hard to validate manually every time.
- Likely to break silently during refactors.
- Small enough to test without heavy scene setup.

Naming:

- Test files/classes: `ComponentNameTests`.
- Test methods: `Scenario_ExpectedResult` or `Method_Scenario_ExpectedResult`.
- Keep test fake/stub classes inside the test file unless shared by multiple test files.

For new foundational components, add at least a smoke-level test for the contract before building more features on top of it.

## When to Pause

Pause and ask the task owner when:

- A task requires changing vendor assets or imported packages.
- A serialized Unity diff is unexpectedly large.
- There are multiple plausible gameplay designs and no source-of-truth doc.
- A dirty user change directly conflicts with the requested change.
