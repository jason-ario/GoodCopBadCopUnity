# Task Recipes

These are quick-start recipes for future Codex sessions. Use them after reading `AGENTS.md` and `Docs/AI/Codex/README.md`.

## Standard Broad Task

1. Run `git status --short`.
2. Read `PROJECT_MAP.md`.
3. Pick one deep context file:
   - Systems/ownership: `SYSTEMS.md`
   - Runtime flow: `GAMEPLAY_FLOWS.md`
   - Multiplayer: `NETCODE_NOTES.md`
   - Content/data: `DATA_AND_CONTENT.md`
4. Inspect the concrete source files listed in that section.
5. Plan the smallest change.
6. Edit code/docs with narrow scope.
7. Verify with compile/tests/Unity/MCP where available.
8. Update the relevant Codex context doc only when the session discovered durable project facts.

## Gameplay Bug in Suspect Flow

Read first:

- `GAMEPLAY_FLOWS.md`, sections 5 and 6.
- `SYSTEMS.md`, "Suspect Loop".
- `NETCODE_NOTES.md`, "NetworkObjectReference Timing".

Inspect likely files:

- `Gameplay/SuspectController.cs`
- `Suspect Controller/SuspectCharacter.cs`
- `Suspect Controller/DailySuspectManager.cs`
- `Anomalies/AnomalyController.cs`
- `Interactables/FolderController.cs`
- `Game Systems/HandOffPoint.cs`

Checks:

- Is this host-only, client-only, or both?
- Is current suspect reference valid on the client at that moment?
- Did a static force/intercept flag leak?
- Did a folder/document NetworkVariable get written on client?
- Does Day 1 override the normal flow?

## Add or Change a Suspect

Read first:

- `DATA_AND_CONTENT.md`, "Suspect Content".
- `SYSTEMS.md`, "Suspect Loop".

Inspect likely assets/scripts:

- `Assets/_GoodCopBadCop/_Data/Suspects`
- Target `SuspectData` asset.
- Target character prefab under `_Prefabs/Characters`.
- `SuspectSet` asset for the intended day.
- `DailySuspectManager.cs`
- `SuspectRunRecords.cs`

Checks:

- Prefab has `NetworkObject` and `SuspectCharacter`.
- `SuspectData.CharacterPrefab` points to the prefab.
- Anomaly category lists are configured on prefab.
- Dialogue arrays exist for the day band.
- First-encounter dialogue should or should not be assigned.
- Killed suspects are excluded from future random lineups.

## Add or Change a Day

Read first:

- `GAMEPLAY_FLOWS.md`, sections 2 and 3.
- `DATA_AND_CONTENT.md`, "Day Content".

Inspect likely files:

- `Game Systems/CampaignManager.cs`
- `Game Systems/DayBase.cs`
- `Game Systems/Days/*.cs`
- Scene object under `CampaignManager` in `Main.unity`
- Relevant `SuspectSet`

Checks:

- Day object is collected by `CampaignManager`.
- `DayNumber` is unique and correct.
- `SuspectSet` is assigned if the day has normal suspects.
- Events subscribed by day scripts are unsubscribed in `DayDeactivated` and `OnDestroy`.
- Static flags like `SuspectController.InterceptNextSuspectSpawn` and `HandOffPoint.BlockVerdict` are reset.
- Day 1 custom flow is not accidentally reused for normal days.

## Multiplayer or Late Join Bug

Read first:

- `NETCODE_NOTES.md`.
- `GAMEPLAY_FLOWS.md`, section 11.

Inspect likely files:

- `Networking/LobbyManager.cs`
- `GameManager.cs`
- `Networking/PlayerSpawner.cs`
- `Player/PlayerInstance.cs`
- Any touched feature system.

Checks:

- Does server write all authoritative NetworkVariables?
- Does the client request changes through ServerRpc?
- Does a new object need `NetworkObject` and network prefab registration?
- Does host mode double-run logic?
- Does late joiner receive current UI/camera/control/gameplay state?
- Is `PlayerInstance.Instance` being used only for local-player decisions?

## Interaction, Pickup, Placement, or Tool Bug

Read first:

- `SYSTEMS.md`, "Player, Interaction, and Equipment".
- `GAMEPLAY_FLOWS.md`, section 9.
- `NETCODE_NOTES.md`, "Held Items and Interaction".

Inspect likely files:

- `Interaction System/PlayerInteractionController.cs`
- `Interaction System/PickableObject.cs`
- `Equipment System/PlayerPickupController.cs`
- `PlacementSystem/*`
- Target item/interactable class.

Checks:

- Empty-hand LMB/E and held-item LMB/E route correctly.
- Compatibility list `itemsThatCanInteractWith` is correct.
- Object held by another player is hidden/blocked.
- Placement board/free placement and slope rules still work.
- Server owns spawn/despawn/holder state.

## UI Flow Change

Read first:

- `SYSTEMS.md`, "UI, Guidebook, Economy, and Shop".
- `GAMEPLAY_FLOWS.md`, section 10.

Inspect likely files:

- `UI/UIController.cs`
- Specific UI screen controller.
- Caller system that opens/closes the UI.

Checks:

- UI open/close is local-only unless explicitly networked.
- Player movement/interaction/cursor lock state is restored.
- Host and client do not both show host-only controls.
- Pause/death/dialogue/scripted mode interactions are checked.

## New Night Threat

Read first:

- `SYSTEMS.md`, "Between-Shift Threats".
- `DATA_AND_CONTENT.md`, "Night Threat Content".

Implement pattern:

- Create a `NetworkBehaviour` implementing `ISystemicThreat`.
- Use server-written NetworkVariables for `ThreatLevel` and any shared state.
- Implement `BeginNightPhase` and `EndNightPhase` as server-only behavior.
- Register component in `BetweenShiftTaskManager._threatBehaviours`.
- Notify `TaskRegistry` when local row labels need refresh.

Checks:

- Threat appears in guidebook/HUD.
- Threat level updates on client.
- `ShiftPerformanceEvaluator` can sample it.
- Cleanup on day start/night end is defined.

## Documentation Update

Use when a session discovers durable project facts.

Where to write:

- Stable subsystem facts: `PROJECT_MAP.md` or `SYSTEMS.md`.
- Runtime sequence facts: `GAMEPLAY_FLOWS.md`.
- Multiplayer patterns: `NETCODE_NOTES.md`.
- Data/content authoring: `DATA_AND_CONTENT.md`.
- Work performed: summarize in the chat or commit message, not in Codex index files.
- Durable architecture/process decisions: update the relevant shared doc in `Docs/AI` only when needed.
- Risks: `KNOWN_RISKS.md`.

Do not hand-edit:

- `Docs/AI/Codex/generated/PROJECT_SNAPSHOT.md`
- `Docs/AI/Codex/generated/PROJECT_SNAPSHOT.json`

Refresh generated files instead.
