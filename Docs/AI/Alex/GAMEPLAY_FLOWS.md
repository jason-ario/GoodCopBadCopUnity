# Gameplay Flows

Last deep survey: 2026-07-04.
Scope: static source inspection; validate in Unity before relying on exact scene object wiring.

## When To Read

Read this when behavior depends on runtime ordering: menu to lobby, campaign start, day activation, shift flow, suspect spawn/verdict, day advance, or late join. For ownership and source-file map only, use `SYSTEMS.md`.

## 1. Menu to Gameplay

Primary files:

- `UI/MainMenuController.cs`
- `UI/StartCampaignScreen.cs`
- `Networking/LobbyManager.cs`
- `GameManager.cs`
- `Networking/PlayerSpawner.cs`
- `Game Systems/SaveDataManager.cs`

Flow:

1. Player chooses campaign/new/continue or multiplayer from `MainMenuController`.
2. New/host flow calls `GameManager.BeginLobbyTransition`, then `LobbyManager.CreateLobby`.
3. `LobbyManager.CreateLobby` starts host through FacepunchTransport/Steam or UnityTransport LAN.
4. `MainMenuController` waits until host is ready.
5. `GameManager.TransitionToLobby` handles fade/audio/menu transition and player lobby spawning.
6. `StartCampaignScreen` displays lobby members, invite code, ready state, and host-only start.
7. Host start calls `SaveDataManager.InitialiseActiveSlot`, then `GameManager.TryStartGame`.
8. `GameManager.StartGameServer` sets game-start state and broadcasts `StartGameClientRpc`.
9. `StartGameClientRpc` starts the campaign on all clients through `CampaignManager.StartCampaign`.

Continue flow:

- `MainMenuController.ContinueGame` requires an active save slot, creates a lobby, then calls `GameManager.TryStartGame(skipTransition: true)`.

Important checks:

- Solo still depends on host/network startup.
- If touching this flow, verify host-only, single-player host, joined client, and late join.

## 2. Campaign and Day Activation

Primary files:

- `Game Systems/CampaignManager.cs`
- `Game Systems/DayBase.cs`
- `Game Systems/Days/Day_01.cs`
- `Game Systems/ShiftManager.cs`
- `Suspect Controller/DailySuspectManager.cs`
- `Suspect Controller/SuspectRunRecords.cs`

Flow:

1. `CampaignManager.StartCampaign` reads `SaveDataManager.Instance.CurrentDay`.
2. Server writes `_networkCurrentDay`.
3. `ApplyDay(day)` deactivates the previous `DayBase`, activates the matching `DayBase`, and sets `ActiveDay`.
4. `CampaignManager` pushes the day number into `ShiftManager.SetCurrentDay`.
5. If the active `DayBase` has a `SuspectSet`, it is given to `DailySuspectManager`.
6. Tutorial steps on the day can be fired through `OnTutorialStepRequested`.
7. The active day's `DayActivated` hook runs.
8. `OnDayChanged` is fired.
9. For days after Day 1, `ShiftManager.OnDoorLock` can be fired based on day locking config.

Day advance:

1. Shift end path eventually calls `ShiftManager.StartInBetweenShiftSequence`.
2. Server calls `CampaignManager.AdvanceDay`.
3. `AdvanceDay` increments day, saves `CurrentDay`, advances `SuspectRunRecords` infection, then `ApplyDay`.

Important checks:

- Day scripts can set global/static state and event subscriptions. Always inspect `DayDeactivated` and `OnDestroy` when changing a day.
- `Day_01` is scripted and should be treated as a custom flow, not just a data configuration.

## 3. Day 1 Tutorial Flow

Primary file:

- `Game Systems/Days/Day_01.cs`

High-level flow:

1. `DayActivated` unlocks the drawer, locks stamps until tutorial permission, exposes documentation notebook, hides mutation/biological notebooks, subscribes to many events, and sets `HandOffPoint.BlockVerdict`.
2. After `ShiftManager.OnDayStart`, `Day1OpeningSequence` opens and locks the shutter, arms `SuspectController.InterceptNextSuspectSpawn` for Vlad, overrides first arrival timing, and auto-starts shift.
3. Vlad arrives with no normal paperwork/entry line. `ScriptedDialogueRunner` plays Vlad dialogue.
4. Vlad paperwork tutorial spawns documents, listens for pickup and folder filing, then grants stamp permission.
5. The stamped folder at the window is held by `HandOffPoint.BlockVerdict`; Day 1 controls when the verdict is actually delivered.
6. After Vlad, Day 1 moves through a random/clean suspect, documentation tutorial suspect, Ivan, and then the soldier/Alexei/mutant sequence.
7. Debug helpers can skip to the soldier slot and bypass the opening sequence.

Important checks:

- Do not add Day 1 behavior only through `SuspectSet` without checking intercepts.
- `HandOffPoint.BlockVerdict` must be reset after deferred verdicts.
- Day 1 subscribes and unsubscribes many static events; duplicate or missed subscriptions can create double dialogue, stale tutorial tasks, or blocked verdicts.

## 4. Shift Start and Suspect Scheduling

Primary files:

- `Game Systems/ShiftManager.cs`
- `Suspect Controller/DailySuspectManager.cs`
- `Gameplay/SuspectController.cs`

Flow:

1. Player/host initiates `ShiftManager.TryStartShift`.
2. Server validates shift state and, in normal cases, all players being inside the booth.
3. `StartShiftServer` sets `shiftStarted` and calls `StartShiftClientRpc`.
4. Client flow opens the window sequence, resets previous suspect state, sets lamps/buzzer/door state, and fires `OnShiftStart`.
5. `DailySuspectManager` listens to `OnShiftStart` and builds `shiftSuspects`.
6. Server schedules the first suspect through `SetNextSuspectReady` and timing overrides.
7. If lineup is exhausted, clock-out is enabled instead of scheduling another suspect.

Important checks:

- Scheduling can be paused or intercepted.
- `ShiftManager.OverrideFirstArrivalInterval`, `OverrideSuspectArrivalInterval`, `PauseSuspectScheduling`, and `_pendingNextSuspect` affect timing.
- Some code still names night-phase helpers as "tasks" even though the active model is systemic threats.

## 5. Suspect Spawn to Paperwork

Primary files:

- `Gameplay/SuspectController.cs`
- `Suspect Controller/DailySuspectManager.cs`
- `Suspect Controller/SuspectCharacter.cs`
- `Anomalies/AnomalyController.cs`
- `Game Systems/SuspectEncounterManager.cs`

Flow:

1. Server calls `SuspectController.NextSuspect`.
2. `WaitAndSpawnNextSuspect` increments `suspectIndex`.
3. A one-shot `InterceptNextSuspectSpawn` can fully replace the normal spawn.
4. Otherwise the index is checked for mutant slot, doppelganger slot, or regular suspect data.
5. Regular spawn instantiates `SuspectData.CharacterPrefab`, requires `NetworkObject`, spawns it, initializes anomalies, and sends `AssignReferencesClientRpc`.
6. Clients may need to wait until SpawnManager resolves the network object before `suspectCharacter` is valid.
7. `InitiateSuspect` moves the suspect to the window position.
8. `ArrivedAtPosition` initializes disabled anomalies, fires `OnSuspectArrived`, rotates the suspect, then starts entry dialogue or waits for booth/shutter state.
9. `SuspectEncounterManager` can intercept first encounters and run `SuspectData.introDialogue`.
10. `SpawnPaperwork` server-spawns ID card/application form, assigns data, tracks documents, and fires `OnPaperworkSpawned`.

Important checks:

- Direct current-suspect access on clients can race. Prefer events/RPC completion points.
- Static force flags such as `ForceNextSuspectClean` and `ForceNextSuspectNoPaperwork` must be consumed/reset carefully.

## 6. Verdict, Scoring, and Payout

Primary files:

- `Game Systems/HandOffPoint.cs`
- `Gameplay/SuspectController.cs`
- `Interactables/FolderController.cs`
- `Tools/AnomalyCategory.cs`
- `Game Systems/ShiftManager.cs`
- `Interactables/ATM.cs`

Flow:

1. Player stamps a folder. `FolderController` syncs stamp type and stamped state.
2. Player places stamped folder at `HandOffPoint`.
3. `HandOffPoint.OnPlaced` calls `SuspectController.DeliverVerdict`, unless `BlockVerdict` is true.
4. `DeliverVerdict` calls `folder.OnHandOff`, disables suspect interaction, and routes to server if needed.
5. Server executes verdict:
   - Calculate checked categories from the folder.
   - Compare against active anomaly categories on the current suspect.
   - Calculate correct, missed, false positive, perfect, and evidence bonus.
   - Spawn payout coupons through `ATM`.
6. Verdict stamp routes to pass, quarantine, or kill sequence.
7. `ShiftManager` tallies passed/quarantined/killed and correct/wrong counts.
8. Folder/documents are cleaned up.
9. Suspect exits, despawns, or runs kill/quarantine machine timeline.
10. `ShiftManager.SetNextSuspectReady` schedules the next slot.

Important checks:

- Category scoring depends on C# type-name strings, not enum values alone.
- `FolderController.OnHandOff` writes server-authoritative network state; the server path repeats it because client writes would be dropped.
- Quarantine marks `SuspectRecord.pendingVaccineReset`; kill marks `SuspectRecord.isKilled`.

## 7. End Shift and Day Advance

Primary files:

- `Game Systems/ShiftManager.cs`
- `Game Systems/CampaignManager.cs`
- `Game Systems/BetweenShiftTaskManager.cs`

Flow:

1. When lineup is done, clock-out becomes available.
2. `ShiftManager.EndShift` runs server-side cleanup, stops suspect scheduling, clears overrides, opens door/window as needed, and broadcasts shift end.
3. `SignalShiftEndClientRpc` fires `OnShiftEnd` and unlocks the door.
4. `StartInBetweenShiftSequence` handles fade/reset/teleport and calls `CompletedShift`.
5. Server calls `CampaignManager.AdvanceDay`.
6. `ShiftManager.OnDayStart` fires for the next day/phase.

Important checks:

- Some comments mention night phase removal, while active `BetweenShiftTaskManager` and systemic threats still exist.
- Treat night/task/threat changes as a mixed legacy area.

## 8. Between-Shift Threat Flow

Primary files:

- `Game Systems/BetweenShiftTaskManager.cs`
- `Game Systems/ISystemicThreat.cs`
- `Game Systems/ShiftPerformanceEvaluator.cs`
- `Guidebook/TaskRegistry.cs`
- `Game Systems/Tasks/*Threat.cs`

Flow:

1. `BetweenShiftTaskManager.BeginNightPhase` is called on all clients.
2. Every client starts a local minimum-night-duration timer.
3. Server calls `BeginNightPhase` on each registered `ISystemicThreat`.
4. Server starts `ShiftPerformanceEvaluator` sampling.
5. All clients register threats in `TaskRegistry`.
6. Threat scripts update NetworkVariables for pressure.
7. Players reduce pressure through world interactions.
8. `EndNightPhase` stops server threat behavior and evaluates performance coupons.

Important checks:

- New work should prefer `ISystemicThreat` over obsolete `IBetweenShiftTask`.
- Some older task scripts still implement `IBetweenShiftTask`; inspect whether they are actually wired before extending them.

## 9. Interaction and Pickup Flow

Primary files:

- `Interaction System/PlayerInteractionController.cs`
- `Interaction System/PickableObject.cs`
- `Equipment System/PlayerPickupController.cs`
- `PlacementSystem/*`

Flow:

1. Local `PlayerInteractionController.Update` exits for non-local players, missing reticle, pause, or disabled interaction.
2. `HandleReticle` raycasts interactable and placement layers, handles highlight/too-far state, and shows/hides placement preview.
3. LMB/E route to `TryWorldInteract` when empty-handed.
4. LMB/E route to `TryItemUse` when holding an item.
5. Item compatibility is checked through `itemsThatCanInteractWith`.
6. Pickups and drops route through `PlayerPickupController` and `PickableObject` RPCs/holder state.
7. Free/board placement uses `ObjectPlacer`, `PlacementBoard`, slope checks, and item placement flags.

Important checks:

- Held item state is networked through `NetworkObjectReference`; allow for resolution delay.
- Do not assume object ownership and "held by client" are the same thing.

## 10. UI Flow

Primary files:

- `UI/UIController.cs`
- `UI/MainMenuController.cs`
- `UI/StartCampaignScreen.cs`
- `Guidebook/*`
- `UI/*`

Common entry points:

- Gameplay HUD: `UIController.ShowPlayerUI` / `ClosePlayerUI`.
- Fade: `FadeIn` / `FadeOut`.
- Tool shop: `OpenToolShop` / `CloseToolShopUI`.
- Start shift: `OpenStartShiftScreen` / `EnterFirstShift`.
- End report: `ShowEndShiftReport` / `HideEndOfShiftReport`.
- Pause: `OpenPauseMenu` / `ClosePauseMenu`.
- Death: `ShowDeathScreen` / `HideDeathScreen`.
- Day end: `OpenEndDayPopup` / `OpenEndDayBlockedPopup`.

Important checks:

- Many gameplay scripts call `UIController.Instance` directly; null-check before adding new call sites.
- UI changes often require local-player gating, because UI is not server-authoritative gameplay state.

## 11. Late Join Flow

Primary files:

- `Networking/LobbyManager.cs`
- `GameManager.cs`
- `Networking/PlayerSpawner.cs`

Flow:

1. Host receives `LobbyManager.OnClientConnected`.
2. If game already started and intro already started:
   - Determine whether host is outside by reading host `PlayerObject`, not `PlayerInstance.Instance`.
   - Spawn new client at lobby or booth accordingly.
   - Call `GameManager.InitializeLateJoinClient`.
3. If game started but intro not started:
   - Spawn at lobby.
   - Call `InitializeLobbyJoinClient`.
4. If game not transitioning:
   - Spawn at lobby.
   - Call `InitializeLobbyJoinClient`.
5. If transition is in progress:
   - Defer spawn to lobby transition sequence.

Important checks:

- Late join is a first-class path. Any new scene state, UI mode, dialogue mode, or player lock should define what late joiners see.
