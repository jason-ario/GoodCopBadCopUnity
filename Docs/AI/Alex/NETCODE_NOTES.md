# Netcode Notes

Last deep survey: 2026-07-04.

## When To Read

Read this before changing any `NetworkBehaviour`, `NetworkVariable`, RPC, spawned prefab, lobby/session path, player ownership path, or gameplay state that can differ between host and client.

## Baseline

This project uses Unity Netcode for GameObjects heavily. A static survey found more than a thousand occurrences of Netcode concepts across product scripts: `NetworkBehaviour`, `NetworkVariable`, `ServerRpc`, `ClientRpc`, `NetworkObjectReference`, and `NetworkObject`.

Assume multiplayer impact until proven otherwise.

## Authority Rules

- Server writes gameplay NetworkVariables.
- Clients request gameplay changes through ServerRpc or owner-authorized methods.
- Server instantiates/despawns networked gameplay objects.
- ClientRpc is used for visual/UI synchronization, animation triggers, dialogue, and client-specific initialization.
- Local-only player/UI logic must be gated with `IsLocalPlayer`, local client id, or target ClientRpc params.

Common server-owned state:

- Current day and shift state: `CampaignManager`, `ShiftManager`.
- Suspect spawn/despawn and verdict execution: `SuspectController`.
- Money: `GlobalHostVariables.money`.
- Folder hand-off and stamp state: `FolderController`.
- Threat levels: `ISystemicThreat` implementations.
- Spawned pickups/documents/coupons: server-instantiated `NetworkObject`s.

## NetworkObject Lifecycle

Rules for spawned gameplay prefabs:

- Runtime-spawned prefabs need a `NetworkObject`.
- Prefabs spawned through NGO must be in NetworkManager's prefab list unless scene-spawned or otherwise registered by project setup.
- Server should call `Spawn(destroyWithScene: true)` or relevant project helper.
- Despawn through `NetworkObject.Despawn` or local helpers like `NetworkHelper` where already used.
- Do not destroy networked objects directly on clients.

Important project examples:

- `SuspectController.SpawnSuspectServer` instantiates suspect prefabs and requires `NetworkObject`.
- `SuspectController.SpawnPaperwork` server-spawns document objects and tracks them for cleanup.
- `ATM.SpawnOneCoupon` spawns coupon `NetworkObject`s.
- `MutantThreat`, `GraffitiThreat`, `FollowTrailThreat`, and similar threat scripts spawn networked world objects.

## NetworkObjectReference Timing

Common pattern:

- Server stores or sends `NetworkObjectReference`.
- Client may not resolve it immediately.
- Client code may need to wait until SpawnManager has the object.

Important examples:

- `SuspectController.AssignReferencesClientRpc` waits for the spawned suspect to exist before assigning `suspectCharacter`.
- `PlayerPickupController._heldObjectRef` syncs held object references and then applies body constraints.

Rule:

- Avoid writing code that assumes a referenced network object exists on the same frame a ClientRpc arrives.

## Host Is Not Just A Client

Host mode runs server and local client in one process. This creates subtle differences:

- Some ClientRpc methods return early on server with `if (IsServer) return;`.
- Some local events fire immediately on host and later through network on clients.
- Some server methods call local state and RPC state in the same flow.

Before changing shared gameplay flow, check host and non-host client paths separately.

## Local Player Singleton

`PlayerInstance.Instance` is a local-player convenience. Do not use it as a reliable reference to the host player or all players.

Known safe pattern:

- For host/client-specific state in late join, inspect `NetworkManager.Singleton.ConnectedClients[clientId].PlayerObject`.

Known risky pattern:

- Server logic reading `PlayerInstance.Instance` to decide another client's state.

## Lobby and Late Join

Primary files:

- `Networking/LobbyManager.cs`
- `GameManager.cs`
- `Networking/PlayerSpawner.cs`

Late-join state branches:

- Game started and intro started: spawn client relative to host outside/booth state, then call `InitializeLateJoinClient`.
- Game started but intro not started: spawn at lobby, call `InitializeLobbyJoinClient`.
- Game not transitioning: spawn at lobby, call `InitializeLobbyJoinClient`.
- Transition in progress: defer to transition sequence.

Any new long-running mode should decide how late joiners enter it.

## Held Items and Interaction

Primary files:

- `Interaction System/PickableObject.cs`
- `Equipment System/PlayerPickupController.cs`
- `Interaction System/PlayerInteractionController.cs`
- `Interactables/FolderController.cs`

Important concepts:

- Held state is not just NetworkObject ownership.
- `PickableObject` tracks holder client id and interactable override.
- `PlayerInteractionController` hides objects held by another player.
- Documents inside a folder held by another player are also treated as controlled by another player.
- Folder documents and evidence are tracked both locally and on the server for cleanup/scoring.

When changing pickup/drop/folder code:

- Test item pickup/drop as host.
- Test item pickup/drop as client.
- Test held item use against compatible and incompatible interactables.
- Test placing items on placement boards and free surfaces.
- Test folder hand-off from client.

## Dialogue and Cutscenes

Primary files:

- `DialogueSystem/DialogueManager.cs`
- `DialogueSystem/ScriptedDialogueRunner.cs`
- `Game Systems/SuspectEncounterManager.cs`

Patterns:

- Dialogue display and choice UI are synchronized through RPCs.
- Scripted dialogue mode can switch cameras, disable normal player control, and trigger animations.
- Player choices are submitted through ServerRpc and rebroadcast.
- First-encounter dialogue is server-triggered but persisted through local `PlayerPrefs`.

Risk:

- Scripted cameras, animation trigger strings, and wobble profile indexes are data-driven. Missing entries are runtime warnings, not compile errors.

## Verification Checklist For Networked Changes

Minimum checks:

- Compile in Unity and wait for compilation to finish.
- Check console for errors and new warnings.
- Host single-player path still works.
- Host plus one client path works if touched code is networked.
- Client can join before game start if touched lobby/start flow.
- Client can late-join after game start if touched spawn/player/UI/dialogue state.
- Client-side interactions route through ServerRpc and do not write server NetworkVariables locally.

When Unity MCP is available:

- Inspect editor state.
- Discover active scene and relevant objects.
- Make code or scene change.
- Wait for compile.
- Check console.
- Enter Play Mode or run targeted scene/test where relevant.
- Take screenshot or inspect object state for UI/visual changes.

## Common Failure Modes

- Writing a NetworkVariable on a client.
- Calling `Destroy` on a spawned network object instead of despawning.
- Missing `NetworkObject` on a prefab spawned by server code.
- Missing NetworkManager prefab registration.
- Assuming a ClientRpc object reference resolves immediately.
- Using `PlayerInstance.Instance` in server code for non-local player decisions.
- Forgetting host mode gets both server and client callbacks.
- Static debug/intercept flags leaking into the next suspect/day.
- Late joiner misses UI state, camera mode, disabled controls, or active dialogue.
