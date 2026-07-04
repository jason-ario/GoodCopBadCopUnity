# Project Map

## High-Level Facts

- Project: `GoodCopBadCopUnity`
- Unity version: `6000.5.1f1`
- Main product root: `Assets/_GoodCopBadCop`
- Main build scene: `Assets/_GoodCopBadCop/_Scenes/Main.unity`
- Secondary build scene entry: `Assets/_GoodCopBadCop/_Scenes/Cutscenes.unity` is present but disabled in build settings.
- Main script root: `Assets/_GoodCopBadCop/_Scripts`
- Main data root: `Assets/_GoodCopBadCop/_Data`
- Main prefab root: `Assets/_GoodCopBadCop/_Prefabs`

## Important Packages

- Unity MCP: `com.coplaydev.unity-mcp`
- Bezi Sidekick package is present locally, but Alex's workflow should stay local-first.
- Netcode for GameObjects: `com.unity.netcode.gameobjects`
- Multiplayer Play Mode: `com.unity.multiplayer.playmode`
- Input System: `com.unity.inputsystem`
- URP/Shader Graph: `com.unity.render-pipelines.universal`, `com.unity.shadergraph`
- Cinemachine: `com.unity.cinemachine`
- Timeline: `com.unity.timeline`
- Test Framework: `com.unity.test-framework`

## Product Content Layout

- `_Scenes`: main scenes, including `Main.unity`, cutscene scenes, and small utility scenes.
- `_Scripts`: gameplay, UI, networking, anomaly, dialogue, interaction, player, and editor scripts.
- `_Data`: ScriptableObject content for suspects, dialogue, pickups, weapons, environments, enemies, and campaign data.
- `_Prefabs`: gameplay prefabs, UI prefabs, interactables, documents, and scene objects.
- `_Settings`: URP assets, volume profiles, renderer settings, and project-specific rendering assets.
- `Timelines` and `Signals`: authored cutscene/timeline content.
- `_Umotion Projects`: animation authoring assets and generated animation-related project data.

## Code Shape

- The project is MonoBehaviour-heavy and singleton-heavy.
- Many gameplay systems use Unity Netcode `NetworkBehaviour`, `ServerRpc`, and `ClientRpc`.
- Current product code appears to compile into `Assembly-CSharp`; no product asmdef was found during the initial survey.
- Editor helper scripts exist under `Assets/_GoodCopBadCop/_Scripts/Editor`.

## Major Gameplay Areas

- Campaign/day flow: `Game Systems`, especially `CampaignManager`, `ShiftManager`, and `Game Systems/Days`.
- Suspects/anomalies: `Gameplay/SuspectController`, `Anomalies`, `Checklist`, `Data/Suspects`.
- Multiplayer/session flow: `Networking`, `Netcode`, `GameManager`, player spawning, RPC-heavy systems.
- Interactions/equipment: `Interactables`, `Interaction System`, `Equipment System`, `Tools`, `Tool Locker`.
- Dialogue/cutscenes: `DialogueSystem`, `Timelines`, `Signals`, Timeline assets.
- UI: `UI`, `Guidebook`, `Newspaper`, `Notebook`, `PC`, reporting/shop/settings UI.

## Vendor and Imported Content

Treat these as vendor/imported/support content unless a task targets them:

- Asset-store or third-party folders such as `Beautify`, `HighlightPlus`, `FronkonGames`, `MeshBaker`, `UMotionEditor`, `PolyFew`, `Lux URP Essentials`, `Screen Damage`, `VolumetricFog2`, `VolumetricLights`, `JMO Assets`, and similar top-level `Assets/*` folders.
- Package code under `Packages/*`.
- Unity-generated folders: `Library`, `Temp`, `Obj`, `Logs`, `UserSettings`.

## Current Notes

- `Assets/_GoodCopBadCop/_Fonts/My_handwriting SDF.asset` was already modified before this AI framework was created.
- `Assets/_Recovery` contains many recovery scenes; do not treat them as source of truth unless asked.
- Some serialized assets mention missing scripts or missing prefab text during a shallow search; verify in Unity before changing them.
