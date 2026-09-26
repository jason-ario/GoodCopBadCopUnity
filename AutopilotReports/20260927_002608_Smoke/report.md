# Autopilot report — FAIL

- Mode: `Smoke`  Label: `menu-smoke`
- Started: 2026-09-27T00:26:08  Duration: -4s
- End reason: Play Mode exited before the run finished.

| Exceptions | Errors | Stalls | Step failures | Invariants | Assists | Warnings |
|---|---|---|---|---|---|---|
| 206 | 0 | 0 | 1 | 0 | 0 | 14 |

Ignored known-noise log lines: 5

## Days

| Day | Outcome | Duration | Suspects | Errors | Stalls | Step failures | Assists |
|---|---|---|---|---|---|---|---|
| 1 | run ended | -7s | 0 | 206 | 0 | 1 | 0 |

## Exception (4 unique)

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, PreShift, first at 13.6s, seen 1x

```
Day_01+<Day1OpeningSequence>d__162.MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/Days/Day_01.cs:1090)
UnityEngine.SetupCoroutine.InvokeMoveNext (System.Collections.IEnumerator enumerator, System.IntPtr returnValueAddress) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, PreShift, first at 18.8s, seen 201x

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
Day 1, PreShift, first at 18.8s, seen 3x

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
Day -1, Boot, first at -3.8s, seen 1x

```
UnityEngine.Object+MarshalledUnityObject.TryThrowEditorNullExceptionObject (UnityEngine.Object unityObj, System.String parameterName) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException (System.Object obj) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Behaviour.set_enabled (System.Boolean value) (at <d5ea965f39d74501b56f4006ef80da34>:0)
GlitchController.OnDisable () (at Assets/_GoodCopBadCop/_Scripts/VFX/GlitchController.cs:177)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

## StepFailed (1 unique)

### [high] Shift did not start in smoke mode.
Day 1, PreShift, first at 16.6s, seen 1x

```
day=1 phase=PreShift campaignComplete=False
shiftStarted=False suspectIndex=-1 processed=0 entityAtWindow=False currentSuspect=- bellReady=False
clockInArmed=False clockOutArmed=False clockedOut=False blockVerdict=True
report=False paused=False dialogueMode=False scripted=False choices=False cutscene=False
canControl=True canInteract=True held=- pos=(0.9, 0.1, -12.5) timeScale=1 cursorVisible=False

```
Screenshot: `001_StepFailed.png`

## Warning (12 unique)

- (3x, Day 1) PlayOneShot was called with a null AudioClip.
- (1x, Day 1) BoxCollider does not support negative scale or size. The effective box size has been forced positive and is likely to give unexpected collision geometry. If you absolutely need to use negative scaling you can use the convex MeshCollider. Scene hierarchy path "Mine/Cube.015"
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (1)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (23)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (12)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (5)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (15)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (21)' has no valid prefab — skipping.
- (1x, Day 1) [Dissonance:Recording] (20:26:25.822) BasicMicrophoneCapture: Insufficient buffer space, requested 16800, clamped to 16383 (dropping 417 samples)
- (1x, Day 1) [Dissonance:Recording] (20:26:25.822) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
- (1x, Day 1) [Dissonance:Recording] (20:26:27.642) BasicMicrophoneCapture: Insufficient buffer space, requested 49440, clamped to 16383 (dropping 33057 samples)
- (1x, Day 1) [Dissonance:Recording] (20:26:27.642) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate

## Timeline

```
[    0.0s] D1 PreShift | Autopilot started — mode=Smoke days=1..last verdicts=Correct
[    3.4s] D1 PreShift | Authored days in scene: [1, 2, 3, 4, 5] — playing until Day 5.
[    3.4s] D1 PreShift | === Day 1 started ===
[    6.5s] D1 PreShift | Smoke: clocking in.
[   13.6s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   16.6s] D1 PreShift | StepFailed: Shift did not start in smoke mode.
[   18.8s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   18.8s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   -3.8s] D-1 Boot | Exception: MissingReferenceException: The object of type 'UnityEngine.Rendering.Volume' has been destroyed but you are still trying to access it.
Your script should either check if it is null or you should not destroy the object.
```
