# Project Map

Last deep survey: 2026-07-04.
Survey method: filesystem and source-code inspection only; Unity Editor was not launched.

## High-Level Facts

- Project: `GoodCopBadCopUnity`
- Unity version: `6000.5.1f1`
- Main product root: `Assets/_GoodCopBadCop`
- Main build scene: `Assets/_GoodCopBadCop/_Scenes/Main.unity`
- Secondary build scene entry: `Assets/_GoodCopBadCop/_Scenes/Cutscenes.unity` is present but disabled in build settings.
- Main script root: `Assets/_GoodCopBadCop/_Scripts`
- Main data root: `Assets/_GoodCopBadCop/_Data`
- Main prefab root: `Assets/_GoodCopBadCop/_Prefabs`

## Deep Context Files

Use this list as a router. Open only the deeper file that matches the current task.

- `SYSTEMS.md` - subsystem ownership and main source files.
- `GAMEPLAY_FLOWS.md` - runtime flow from menu, lobby, campaign, shift, suspect, verdict, and day advance.
- `NETCODE_NOTES.md` - multiplayer authority and common Netcode failure modes.
- `DATA_AND_CONTENT.md` - where content lives and what assets drive gameplay.
- `TASK_RECIPES.md` - practical checklists for future Codex/MCP tasks.

## Important Packages

- Unity MCP: `com.coplaydev.unity-mcp`
- Bezi Sidekick package is present locally, but this Codex context does not require Bezi or change Bezi users' workflow.
- Netcode for GameObjects: `com.unity.netcode.gameobjects`
- Multiplayer Play Mode: `com.unity.multiplayer.playmode`
- Input System: `com.unity.inputsystem`
- URP/Shader Graph: `com.unity.render-pipelines.universal`, `com.unity.shadergraph`
- Cinemachine: `com.unity.cinemachine`
- Timeline: `com.unity.timeline`
- Test Framework: `com.unity.test-framework`

## Product Content Layout

- `_Scenes`: main scenes, including `Main.unity`, disabled cutscene build scene entries, and utility scenes.
- `_Scripts`: gameplay, UI, networking, anomaly, dialogue, interaction, player, tools, enemies, guidebook, and editor scripts.
- `_Data`: ScriptableObject content for suspects, dialogue, doppelgangers, pickups, weapons, environments, enemies, newspapers, words, shop actions, and campaign data.
- `_Prefabs`: gameplay prefabs, UI prefabs, interactables, documents, characters, shop, tasks, timelines, equipment, weapons, and scene objects.
- `_Settings`: URP assets, volume profiles, renderer settings, and project-specific rendering assets.
- `Timelines` and `Signals`: authored cutscene/timeline content.
- `_Umotion Projects`: animation authoring assets and generated animation-related project data.

## Code Shape

- The project is MonoBehaviour-heavy and singleton-heavy.
- Many gameplay systems use Unity Netcode `NetworkBehaviour`, `ServerRpc`, and `ClientRpc`.
- Current product code appears to compile into `Assembly-CSharp`; no product asmdef was found during the initial survey.
- Editor helper scripts exist under `Assets/_GoodCopBadCop/_Scripts/Editor`.
- Input code in core interaction/player scripts currently uses legacy `Input.*` calls even though the Input System package is installed.
- Gameplay state is split between scene singletons, NetworkVariables, ScriptableObjects, `SaveDataManager` JSON slots, and some `PlayerPrefs` state.

## Major Gameplay Areas

### Session and Menu

- `Assets/_GoodCopBadCop/_Scripts/GameManager.cs` is the global runtime transition manager.
- `Assets/_GoodCopBadCop/_Scripts/Networking/LobbyManager.cs` owns Steam/Facepunch or LAN lobby setup and late-join client spawning decisions.
- `Assets/_GoodCopBadCop/_Scripts/Networking/PlayerSpawner.cs` owns player prefab spawn positions for lobby, booth, outside, and explicit points.
- `Assets/_GoodCopBadCop/_Scripts/Networking/PlayerReadyManager.cs` syncs lobby ready state.
- `Assets/_GoodCopBadCop/_Scripts/UI/MainMenuController.cs` owns main menu screens, save-slot start/continue, and host/lobby transition calls.
- `Assets/_GoodCopBadCop/_Scripts/UI/StartCampaignScreen.cs` owns pre-game lobby UI, ready buttons, invite code display, and host start.

### Campaign, Days, and Shift

- `Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs` is the campaign/day orchestrator.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/DayBase.cs` is the per-day base class for day-specific config and hooks.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/Days/Day_01.cs` is a large scripted tutorial day with its own suspect intercepts, dialogue, stamp locks, and soldier event.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs` owns start/end shift state, door/window/clock-out readiness, suspect scheduling, and day advance.
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/DailySuspectManager.cs` builds the shift suspect lineup and injects mutant/doppelganger slots.

### Suspects, Anomalies, Verdicts

- `Assets/_GoodCopBadCop/_Scripts/Gameplay/SuspectController.cs` is the central suspect loop: spawn, arrival, paperwork, verdict, payout, exit/despawn, mutant intruder.
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectCharacter.cs` holds suspect data, anomaly initialization, dialogue helpers, combat/death, vaccine, and junk pickup routing.
- `Assets/_GoodCopBadCop/_Scripts/Suspect Controller/SuspectRunRecords.cs` stores per-run infection records, killed flags, and quarantine reset state.
- `Assets/_GoodCopBadCop/_Scripts/Anomalies/AnomalyController.cs` selects and syncs active anomalies.
- `Assets/_GoodCopBadCop/_Scripts/Tools/AnomalyCategory.cs` maps the five checklist categories to C# base type names.
- `Assets/_GoodCopBadCop/_Scripts/Interactables/FolderController.cs` owns folder documents, evidence, stamp sync, category checks, and hand-off state.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/HandOffPoint.cs` triggers `SuspectController.DeliverVerdict` when a stamped folder is placed at the window.

### Player, Interaction, Equipment

- `Assets/_GoodCopBadCop/_Scripts/Player/PlayerInstance.cs` is the local player facade and local singleton.
- `Assets/_GoodCopBadCop/_Scripts/Player/PlayerMovementController.cs` owns FPS movement, camera look, sitting/crouch, movement locks, and proxy camera sync.
- `Assets/_GoodCopBadCop/_Scripts/Player/PlayerAnimationController.cs` owns IK, networked body/head/look animation state, arm layers, and spectator animation behavior.
- `Assets/_GoodCopBadCop/_Scripts/Interaction System/PlayerInteractionController.cs` owns raycast interaction, reticle state, held-item use, alternate interact, and placement preview.
- `Assets/_GoodCopBadCop/_Scripts/Interaction System/PickableObject.cs` is the base networked held-item class.
- `Assets/_GoodCopBadCop/_Scripts/Equipment System/PlayerPickupController.cs` owns pickup, drop, use, purchase, spawn-and-pickup, backpack/release, and held-object NetworkObjectReferences.

### Dialogue and Cutscenes

- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/DialogueManager.cs` owns networked line display, audio chunks, subtitles, choices, and wait-for-input.
- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/ScriptedDialogue.cs` is the ScriptableObject format for monologue/choice nodes.
- `Assets/_GoodCopBadCop/_Scripts/DialogueSystem/ScriptedDialogueRunner.cs` owns scripted dialogue mode, camera overrides, wobble profiles, choices, and animation trigger RPCs.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/SuspectEncounterManager.cs` intercepts first encounters and persists them through `PlayerPrefs`.

### UI, Guidebook, Economy, Shop

- `Assets/_GoodCopBadCop/_Scripts/UI/UIController.cs` is the main UI facade for player HUD, fades, shops, reports, pause, invite panel, death screen, and day-end popup.
- `Assets/_GoodCopBadCop/_Scripts/Guidebook/TaskRegistry.cs` is the current runtime registry for guidebook/HUD threat rows.
- `Assets/_GoodCopBadCop/_Scripts/Gameplay/GlobalHostVariables.cs` owns shared networked money/coupon balance.
- `Assets/_GoodCopBadCop/_Scripts/Interactables/ATM.cs` dispenses coupon pickups.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/SaveDataManager.cs` owns three JSON save slots in `Application.persistentDataPath`.
- `Assets/_GoodCopBadCop/_Scripts/Tool Locker/ToolLockerDiegeticController.cs`, `ToolShopController.cs`, `WorldShopItemInteractable.cs`, and `ShopItem` assets drive tool purchases/unlocks.

### Night Phase and Threats

- `Assets/_GoodCopBadCop/_Scripts/Game Systems/BetweenShiftTaskManager.cs` now manages systemic threats, not old discrete tasks.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/ISystemicThreat.cs` is the active interface.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/IBetweenShiftTask.cs` is obsolete compatibility.
- Active threat examples: `MutantThreat`, `FenceThreat`, `GraffitiThreat`, `FollowTrailThreat`, and `TakeOutTrashTask`.
- `Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftPerformanceEvaluator.cs` samples threat levels and awards performance coupons.

## Vendor and Imported Content

Treat these as vendor/imported/support content unless a task targets them:

- Asset-store or third-party folders such as `Beautify`, `HighlightPlus`, `FronkonGames`, `MeshBaker`, `UMotionEditor`, `PolyFew`, `Lux URP Essentials`, `Screen Damage`, `VolumetricFog2`, `VolumetricLights`, `JMO Assets`, and similar top-level `Assets/*` folders.
- Package code under `Packages/*`.
- Unity-generated folders: `Library`, `Temp`, `Obj`, `Logs`, `UserSettings`.

## Current Notes

- `Assets/_Recovery` contains many recovery scenes; do not treat them as source of truth unless asked.
- Some serialized assets mention missing scripts or missing prefab text during a shallow search; verify in Unity before changing them.
- Always run `git status --short` before edits. The deep survey started from a clean worktree.
