# Systems Map

Last deep survey: 2026-07-04.
Scope: static source inspection under `Assets/_GoodCopBadCop/_Scripts`; no Unity Editor run.

## When To Read

Read this when the task needs ownership, coupling, or likely source files for an existing gameplay/UI/player/content subsystem.

## Mental Model

The project is a networked Unity gameplay project built around scene singletons and server-authoritative state. The main runtime chain is:

`MainMenuController` / `StartCampaignScreen` -> `LobbyManager` -> `GameManager` -> `CampaignManager` -> `DayBase` / `ShiftManager` -> `DailySuspectManager` -> `SuspectController` -> `SuspectCharacter` / `FolderController` / `AnomalyController` -> UI, dialogue, payout, and day advance.

Most systems are not isolated services. They communicate through singletons, C# events, NetworkVariables, ServerRpcs/ClientRpcs, Timeline callbacks, and scene object references.

## Session and Lobby

Source files:

- `Assets/_GoodCopBadCop/_Scripts/GameManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Networking/LobbyManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Networking/PlayerSpawner.cs`
- `Assets/_GoodCopBadCop/_Scripts/Networking/PlayerReadyManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/UI/MainMenuController.cs`
- `Assets/_GoodCopBadCop/_Scripts/UI/StartCampaignScreen.cs`

Responsibilities:

- `MainMenuController` switches menu screens, starts save-slot flows, creates lobbies, and calls game/lobby transitions.
- `LobbyManager` initializes Steam, creates Steam lobbies through FacepunchTransport or LAN through UnityTransport, joins by lobby id, LAN address, or join code, and handles late client connections.
- `StartCampaignScreen` shows lobby members, ready state, invite code, and host-only start.
- `GameManager` owns runtime state flags like `HasGameStarted`, `HasIntroCutsceneStarted`, and `IsTransitioningToLobby`, starts the game on the server, spawns/teleports players, and initializes late joiners.
- `PlayerSpawner` chooses lobby, booth, outside, and explicit spawn points and sets player outside/inside state.

Notes for future work:

- Starting even "solo" gameplay still goes through lobby/host creation.
- Late join behavior is split between `LobbyManager.OnClientConnected` and `GameManager.InitializeLateJoinClient` / `InitializeLobbyJoinClient`.
- Any change to lobby transition must be checked in host, client, waiting-lobby, already-started-game, and transition-in-progress cases.

## Campaign and Day Layer

Source files:

- `Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/DayBase.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Days/Day_01.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/DailySuspectManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectRunRecords.cs`

Responsibilities:

- `CampaignManager` is the day orchestrator. It collects child `DayBase` objects, reads/saves current day through `SaveDataManager`, activates one day, injects that day's `SuspectSet` into `DailySuspectManager`, forwards day number to `ShiftManager`, advances infection records, and fires day/tutorial events.
- `DayBase` is a per-day scene component with inspector fields for day number, intro cutscene, suspect set, lock-door behavior, tutorial steps, supply boxes, and follow-trail eligibility.
- `Day_01` is not a normal data-only day. It locks/unlocks stamps and notebooks, arms scripted suspect intercepts, runs Vlad/Ivan tutorials, blocks and later delivers Vlad's hand-off verdict, and starts the soldier/Alexei/mutant sequence.
- `ShiftManager` starts and ends shifts, opens the booth window, locks/unlocks doors, schedules suspects, handles clock-out readiness, triggers day advance, and still has partial night-phase hooks.
- `DailySuspectManager` builds the lineup on `ShiftManager.OnShiftStart`. It can use a day override, picks random suspects from the active `SuspectSet`, removes killed suspects, and injects mutant/doppelganger slots after Day 1.
- `SuspectRunRecords` tracks living suspects, infection score, killed state, and quarantine reset state for the run.

Key coupling:

- `CampaignManager.ApplyDay` calls `ShiftManager.SetCurrentDay` and `DailySuspectManager.SetSuspectSet`.
- `ShiftManager.SetNextSuspectReady` eventually calls `SuspectController.NextSuspect`.
- `CampaignManager.AdvanceDay` calls `SuspectRunRecords.AdvanceDayInfection`.
- Day scripts subscribe heavily to global events and static flags in `SuspectController`, `FolderController`, `ExamNotebook`, `HandOffPoint`, and `ShiftManager`.

## Suspect Loop

Source files:

- `Assets/_GoodCopBadCop/_Scripts/Gameplay/SuspectController.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectCharacter.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/DailySuspectManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectRunRecords.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectEncounterManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Anomalies/AnomalyController.cs`
- `Assets/_GoodCopBadCop/_Scripts/Interactables/FolderController.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/HandOffPoint.cs`

Responsibilities:

- `SuspectController` is the central booth loop. It owns current lineup index, current suspect reference, spawned documents, spawned folder, payout scoring fields, and special paths for scripted suspects, scene suspects, doppelgangers, and mutant intruders.
- `SuspectCharacter` is the spawned character object. It owns `SuspectData`, anomaly initialization, dialogue data access, first-person interaction routing, vaccine application, combat/death, and collectible corpse behavior.
- `AnomalyController` picks active anomalies from category lists. It has paths for infection score, exact anomaly count, documentation-only tutorial suspects, doppelganger initialization placeholder, and clean initialization.
- `FolderController` owns stamped verdict state, document/evidence tracking, queue slots, category checklist extraction, networked hand-off state, and cleanup of server-spawned documents.
- `HandOffPoint` watches stamped folders placed at the window and calls `SuspectController.DeliverVerdict`, unless `HandOffPoint.BlockVerdict` is set by a scripted day.

Important suspect paths:

- Regular path: `ShiftManager.SetNextSuspectReady` -> `SuspectController.NextSuspect` -> `WaitAndSpawnNextSuspect` -> `SpawnSuspectServer` -> `InitiateSuspect` -> `ArrivedAtPosition` -> entry dialogue -> paperwork -> verdict.
- Scripted path: static `SuspectController.InterceptNextSuspectSpawn` bypasses the normal lineup once.
- First encounter path: `SuspectEncounterManager.TryInterceptForIntroDialogue` runs `SuspectData.introDialogue` once per suspect asset name through `PlayerPrefs`, suppressing generic bark and paperwork until dialogue ends.
- Mutant slot path: `DailySuspectManager.IsMutantSlot` lets `SuspectController` spawn `MutantSuspectBehaviour` instead of a normal suspect.
- Doppelganger slot path: `DailySuspectManager.IsDoppelgangerSlot` spawns target suspect prefab and calls `SuspectCharacter.InitializeAsDoppelganger`.

Scoring:

- `SuspectController.CalculateCategoryScores` compares folder checked category type names against active anomaly categories.
- Categories are string type names: `DocumentationAnomaly`, `VitalsAnomaly`, `BehaviorAnomaly`, `MutationAnomaly`, `SupernaturalAnomaly`.
- `FolderController.GetEvidenceCountByCategory` contributes evidence bonus when evidence matches correctly identified categories.
- `SuspectController.PayOutResults` spawns coupons through `ATM.Instance.SpawnCoupons` and sends UI popups.

## Player, Interaction, and Equipment

Source files:

- `Assets/_GoodCopBadCop/_Scripts/Player/PlayerInstance.cs`
- `Assets/_GoodCopBadCop/_Scripts/Player/PlayerMovementController.cs`
- `Assets/_GoodCopBadCop/_Scripts/Player/PlayerAnimationController.cs`
- `Assets/_GoodCopBadCop/_Scripts/Interaction System/PlayerInteractionController.cs`
- `Assets/_GoodCopBadCop/_Scripts/Interaction System/PickableObject.cs`
- `Assets/_GoodCopBadCop/_Scripts/Equipment System/PlayerPickupController.cs`
- `Assets/_GoodCopBadCop/_Scripts/PlacementSystem/*`

Responsibilities:

- `PlayerInstance` is a local-player facade and local singleton. It gates movement/interaction, outside/inside state, death/respawn/spectating, player health/radiation/camera, and reticle access.
- `PlayerMovementController` handles CharacterController movement, camera look, crouch/sit, control locks, and networked proxy camera position/pitch.
- `PlayerAnimationController` handles the large IK/body animation layer: head/chest look, held item layers, rig weights, arm IK, lean, spectator mode, and networked animation bools/triggers.
- `PlayerInteractionController` handles local raycasts, reticle state, too-far state, left click/E interaction, item-on-interactable use, placement preview, and blocking objects controlled by another player.
- `PickableObject` is the base for networked pickup items. It separates ownership, holder client id, interactable state, parent/socket constraints, placement/drop RPCs, and despawn helpers.
- `PlayerPickupController` owns held object state, spawned/purchased item pickup, body/camera equipped containers, use/drop/release, and `_heldObjectRef` NetworkObjectReference replication.

Interaction input:

- LMB with empty hands: primary world interaction.
- E with empty hands: alternate interaction.
- LMB or E while holding item: item-use path first.
- RMB while holding item: placement preview through `ObjectPlacer`, slope checks, placement boards, and per-item placement restrictions.

Risk:

- Do not assume `PlayerInstance.Instance` is a global host player. It is a local-player convenience.
- Do not interact with a `PickableObject` held by another player or a folder item inside another player's folder; `PlayerInteractionController.IsControlledByOtherPlayer` explicitly hides those.

## Dialogue and Cutscene Systems

Source files:

- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/DialogueManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/ScriptedDialogue.cs`
- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/ScriptedDialogueRunner.cs`
- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/SpeakingInteraction.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/SuspectEncounterManager.cs`
- `Assets/_GoodCopBadCop/Timelines`
- `Assets/_GoodCopBadCop/Signals`

Responsibilities:

- `DialogueManager` displays networked subtitles, plays audio chunks, handles skip/reveal/wait-for-input, and can show dialogue choices.
- `ScriptedDialogue` is the authored data object with monologue or choice nodes, NPC line, camera trigger, animation trigger, wobble override, and choices.
- `ScriptedDialogueRunner` runs a whole scripted dialogue in networked scripted mode. It disables normal player mode as needed, switches override cameras by key, triggers speaker animations, shows player choices, and exits scripted mode at the end unless deferred.
- `SuspectEncounterManager` runs first-meeting `ScriptedDialogue` from `SuspectData.introDialogue` once per suspect asset name.

Risk:

- Scripted dialogue camera keys must exist in `ScriptedDialogueRunner._cameras`.
- Choice submission is server-routed. Verify host and client behavior for any dialogue choices.
- First-encounter state uses `PlayerPrefs`, not `SaveDataManager`.

## UI, Guidebook, Economy, and Shop

Source files:

- `Assets/_GoodCopBadCop/_Scripts/UI/UIController.cs`
- `Assets/_GoodCopBadCop/_Scripts/UI/MainMenuController.cs`
- `Assets/_GoodCopBadCop/_Scripts/UI/StartCampaignScreen.cs`
- `Assets/_GoodCopBadCop/_Scripts/Guidebook/TaskRegistry.cs`
- `Assets/_GoodCopBadCop/_Scripts/Guidebook/*`
- `Assets/_GoodCopBadCop/_Scripts/Gameplay/GlobalHostVariables.cs`
- `Assets/_GoodCopBadCop/_Scripts/Interactables/ATM.cs`
- `Assets/_GoodCopBadCop/_Scripts/Tool Locker/*`

Responsibilities:

- `UIController` is the gameplay UI facade. Use it for fades, player HUD, tool shop, HQ order screen, end-shift report, start-shift screen, guard purchase, shop popup, invite panel, pause, cash/shop notifications, booth waiting, death screen, and day-end popup.
- `TaskRegistry` stores current `ISystemicThreat` rows for guidebook/HUD task display. It self-instantiates and has obsolete task compatibility methods.
- `GlobalHostVariables.money` is a server-written NetworkVariable shared by players.
- `ATM` dispenses physical coupon pickups from rewards.
- Tool shop behavior is split between diegetic locker/world interactables, UI shop controllers, `ShopItem` data, `ShopPurchaseAction`, and `SaveDataManager` unlock state.

## Between-Shift Threats

Source files:

- `Assets/_GoodCopBadCop/_Scripts/Game Systems/BetweenShiftTaskManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/ISystemicThreat.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftPerformanceEvaluator.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Tasks/MutantThreat.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Tasks/FenceThreat.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Tasks/GraffitiThreat.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Tasks/FollowTrailThreat.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Tasks/TakeOutTrashTask.cs`

Current model:

- The active model is systemic threat pressure, not old discrete task completion.
- `BetweenShiftTaskManager` starts the night phase, begins threat behavior on the server, registers threats in `TaskRegistry` on all clients, runs a local minimum-duration timer, and asks `ShiftPerformanceEvaluator` to sample and award coupons.
- `IBetweenShiftTask` and old task classes exist for compatibility but should not be the first choice for new work.

Threat examples:

- `MutantThreat`: active enemy count pressure, mutant bit drops while night phase is active.
- `FenceThreat`: damages fences after a configured day threshold; damage persists into day shift.
- `GraffitiThreat`: spawns graffiti continuously and tracks active graffiti pressure.
- `FollowTrailThreat`: spawns a corpse/trail/destination event when the active day allows it; resolving it can immediately signal night readiness.
- `TakeOutTrashTask`: newer threat-style object registered by Alexei/Day 1 sequence, despite task naming.

## Persistence

Source files:

- `Assets/_GoodCopBadCop/_Scripts/Game Systems/SaveDataManager.cs`
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectRunRecords.cs`
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/SuspectEncounterManager.cs`

Persistence layers:

- `SaveDataManager` writes `savedata.json` under `Application.persistentDataPath`.
- There are three save slots with occupied flag, slot name, tutorial flags, current day, total cash earned, unlocked shop items, unlocked lock ids, and last-saved timestamp.
- `SuspectRunRecords` is runtime memory initialized from `SuspectSet`, not currently documented as save-file persistence.
- `SuspectEncounterManager` stores first-encounter flags in `PlayerPrefs` by suspect asset name.

When changing progression, check which persistence layer owns the value.
