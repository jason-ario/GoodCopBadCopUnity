# Autopilot report — FAIL

- Mode: `Input`  Label: `menu-input`
- Started: 2026-09-27T00:25:03  Duration: -3s
- End reason: Play Mode exited before the run finished.

| Exceptions | Errors | Stalls | Step failures | Invariants | Assists | Warnings |
|---|---|---|---|---|---|---|
| 205 | 0 | 0 | 0 | 0 | 0 | 8 |

Ignored known-noise log lines: 3

## Days

| Day | Outcome | Duration | Suspects | Errors | Stalls | Step failures | Assists |
|---|---|---|---|---|---|---|---|
| 1 | run ended | -6s | 0 | 205 | 0 | 0 | 0 |

## Exception (3 unique)

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, PreShift, first at 14.3s, seen 1x

```
Day_01+<Day1OpeningSequence>d__162.MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/Days/Day_01.cs:1090)
UnityEngine.SetupCoroutine.InvokeMoveNext (System.Collections.IEnumerator enumerator, System.IntPtr returnValueAddress) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

### [high] NullReferenceException: Object reference not set to an instance of an object
Day -1, PreShift, first at -3.3s, seen 201x

```
Unity.Netcode.Components.NetworkRigidbodyBase.OnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkRigidBodyBase.cs:1052)
Unity.Netcode.NetworkBehaviour.InternalOnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:906)
UnityEngine.Debug:LogException(Exception)
Unity.Netcode.NetworkBehaviour:InternalOnNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:910)
Unity.Netcode.NetworkObject:InvokeBehaviourNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkObject.cs:2935)
Unity.Netcode.NetworkSpawnManager:OnDespawnObject(NetworkObject, Boolean, Boolean) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1767)
Unity.Netcode.NetworkSpawnManager:DespawnAndDestroyNetworkObjects() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1538)
Unity.Netcode.NetworkManager:ShutdownInternal() (at ./Library/PackageCache/com.unity.netcod…
```

### [high] NullReferenceException: Object reference not set to an instance of an object
Day -1, PreShift, first at -3.3s, seen 3x

```
Unity.Netcode.Components.NetworkRigidbodyBase.OnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkRigidBodyBase.cs:1054)
Unity.Netcode.NetworkBehaviour.InternalOnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:906)
UnityEngine.Debug:LogException(Exception)
Unity.Netcode.NetworkBehaviour:InternalOnNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:910)
Unity.Netcode.NetworkObject:InvokeBehaviourNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkObject.cs:2935)
Unity.Netcode.NetworkSpawnManager:OnDespawnObject(NetworkObject, Boolean, Boolean) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1767)
Unity.Netcode.NetworkSpawnManager:DespawnAndDestroyNetworkObjects() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1538)
Unity.Netcode.NetworkManager:ShutdownInternal() (at ./Library/PackageCache/com.unity.netcod…
```

## Warning (6 unique)

- (3x, Day 1) PlayOneShot was called with a null AudioClip.
- (1x, Day 1) BoxCollider does not support negative scale or size. The effective box size has been forced positive and is likely to give unexpected collision geometry. If you absolutely need to use negative scaling you can use the convex MeshCollider. Scene hierarchy path "Mine/Cube.015"
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (4)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (24)' has no valid prefab — skipping.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.936001, 0.003230, -12.512001 ) to Target position ( -3.096357, -0.047890, -16.895000 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) Physics.ClosestPoint can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.

## Timeline

```
[    0.0s] D1 PreShift | Autopilot started — mode=Input days=1..last verdicts=Correct
[    2.9s] D1 PreShift | Authored days in scene: [1, 2, 3, 4, 5] — playing until Day 5.
[    2.9s] D1 PreShift | Virtual gamepad attached.
[    2.9s] D1 PreShift | === Day 1 started ===
[    2.9s] D1 PreShift | Clocking in.
[   14.3s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   -3.3s] D-1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   -3.3s] D-1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
```
