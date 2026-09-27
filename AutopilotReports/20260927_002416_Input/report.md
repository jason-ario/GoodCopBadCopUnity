# Autopilot report — FAIL

- Mode: `Input`  Label: `menu-input`
- Started: 2026-09-27T00:24:16  Duration: -6s
- End reason: Play Mode exited before the run finished.

| Exceptions | Errors | Stalls | Step failures | Invariants | Assists | Warnings |
|---|---|---|---|---|---|---|
| 206 | 0 | 0 | 0 | 0 | 0 | 50 |

Ignored known-noise log lines: 3

## Days

| Day | Outcome | Duration | Suspects | Errors | Stalls | Step failures | Assists |
|---|---|---|---|---|---|---|---|
| 1 | run ended | -10s | 0 | 206 | 0 | 0 | 0 |

## Exception (5 unique)

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, PreShift, first at 13.2s, seen 1x

```
Day_01+<Day1OpeningSequence>d__162.MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/Days/Day_01.cs:1090)
UnityEngine.SetupCoroutine.InvokeMoveNext (System.Collections.IEnumerator enumerator, System.IntPtr returnValueAddress) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

### [high] UnassignedReferenceException: The variable <m_InternalRigidbody>k__BackingField of NetworkRigidbodyBase has not been assigned.
You probably need to assign the <m_InternalRigidbody>k__BackingField variable of the NetworkRigidbodyBase script in the inspector.
Day 1, PreShift, first at 30.2s, seen 2x

```
UnityEngine.Object+MarshalledUnityObject.TryThrowEditorNullExceptionObject (UnityEngine.Object unityObj, System.String parameterName) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException (System.Object obj) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Rigidbody.set_interpolation (UnityEngine.RigidbodyInterpolation value) (at <b1e6b0740eef462198babee197c3a375>:0)
Unity.Netcode.Components.NetworkRigidbodyBase.OnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkRigidBodyBase.cs:1054)
Unity.Netcode.NetworkBehaviour.InternalOnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:906)
UnityEngine.Debug:LogException(Exception)
Unity.Netcode.NetworkBehaviour:InternalOnNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:910)
Unity.Netcode.NetworkObject:InvokeBehaviourNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkObject.cs:2935)
Unity.Netcode.NetworkSpawnManager:OnDespawnObject(…
```

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, PreShift, first at 30.2s, seen 200x

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
Day 1, PreShift, first at 30.2s, seen 2x

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

### [high] MissingReferenceException: The object of type 'UnityEngine.Rendering.Volume' has been destroyed but you are still trying to access it.
Your script should either check if it is null or you should not destroy the object.
Day 1, Boot, first at -6.1s, seen 1x

```
UnityEngine.Object+MarshalledUnityObject.TryThrowEditorNullExceptionObject (UnityEngine.Object unityObj, System.String parameterName) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException (System.Object obj) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Behaviour.set_enabled (System.Boolean value) (at <d5ea965f39d74501b56f4006ef80da34>:0)
GlitchController.OnDisable () (at Assets/_GoodCopBadCop/_Scripts/VFX/GlitchController.cs:177)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

## Warning (13 unique)

- (37x, Day 1) Physics.ClosestPoint can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.
- (2x, Day 1) PlayOneShot was called with a null AudioClip.
- (1x, Day 1) BoxCollider does not support negative scale or size. The effective box size has been forced positive and is likely to give unexpected collision geometry. If you absolutely need to use negative scaling you can use the convex MeshCollider. Scene hierarchy path "Mine/Cube.015"
- (1x, Day 1) [Dissonance:Recording] (20:24:17.431) CapturePipelineManager: Forcing capture pipeline reset
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (25)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (9)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (10)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (19)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (15)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (27)' has no valid prefab — skipping.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.936001, 0.003230, -12.512001 ) to Target position ( -3.096357, -0.047890, -16.895000 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) [Dissonance:Recording] (20:24:47.036) BasicMicrophoneCapture: Insufficient buffer space, requested 43200, clamped to 16383 (dropping 26817 samples)
- (1x, Day 1) [Dissonance:Recording] (20:24:47.038) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate

## Timeline

```
[    0.0s] D1 PreShift | Autopilot started — mode=Input days=1..last verdicts=Correct
[    4.0s] D1 PreShift | Authored days in scene: [1, 2, 3, 4, 5] — playing until Day 5.
[    4.0s] D1 PreShift | Virtual gamepad attached.
[    4.0s] D1 PreShift | === Day 1 started ===
[    4.0s] D1 PreShift | Clocking in.
[   13.2s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   30.2s] D1 PreShift | Exception: UnassignedReferenceException: The variable <m_InternalRigidbody>k__BackingField of NetworkRigidbodyBase has not been assigned.
You probably need to assign the <m_InternalRigidbody>k__BackingField variable of the NetworkRigidbodyBase script in the inspector.
[   30.2s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   30.2s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   -6.1s] D1 Boot | Exception: MissingReferenceException: The object of type 'UnityEngine.Rendering.Volume' has been destroyed but you are still trying to access it.
Your script should either check if it is null or you should not destroy the object.
```
