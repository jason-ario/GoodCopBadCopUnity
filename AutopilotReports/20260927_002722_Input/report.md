# Autopilot report — FAIL

- Mode: `Input`  Label: `menu-input`
- Started: 2026-09-27T00:27:22  Duration: -4s
- End reason: Play Mode exited before the run finished.

| Exceptions | Errors | Stalls | Step failures | Invariants | Assists | Warnings |
|---|---|---|---|---|---|---|
| 207 | 0 | 0 | 6 | 0 | 4 | 941 |

Ignored known-noise log lines: 14

## Days

| Day | Outcome | Duration | Suspects | Errors | Stalls | Step failures | Assists |
|---|---|---|---|---|---|---|---|
| 1 | run ended | -7s | 0 | 207 | 0 | 6 | 4 |

## Exception (3 unique)

### [high] UnassignedReferenceException: The variable Hips of LegsAnimator has not been assigned.
You probably need to assign the Hips variable of the LegsAnimator script in the inspector.
Day 1, PreShift, first at 12.5s, seen 2x

```
UnityEngine.Object+MarshalledUnityObject.TryThrowEditorNullExceptionObject (UnityEngine.Object unityObj, System.String parameterName) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Bindings.ThrowHelper.ThrowNullReferenceException (System.Object obj) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.Transform.get_localPosition () (at <d5ea965f39d74501b56f4006ef80da34>:0)
FIMSpace.FProceduralAnimation.LegsAnimator+HipsReference.Initialize (FIMSpace.FProceduralAnimation.LegsAnimator owner, UnityEngine.Transform bone, UnityEngine.Transform root) (at Assets/FImpossible Creations/Plugins - Animating/Legs Animator/Core/Hips Algorithms/LegsA.Hips.Reference.cs:57)
FIMSpace.FProceduralAnimation.LegsAnimator.Initialize () (at Assets/FImpossible Creations/Plugins - Animating/Legs Animator/Core/LegsA.Initialization.cs:51)
FIMSpace.FProceduralAnimation.LegsAnimator.Start () (at Assets/FImpossible Creations/Plugins - Animating/Legs Animator/LegsAnimator.cs:46)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, Shift, first at 164.6s, seen 202x

```
Unity.Netcode.Components.NetworkRigidbodyBase.OnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkRigidBodyBase.cs:1052)
Unity.Netcode.NetworkBehaviour.InternalOnNetworkDespawn () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:906)
UnityEngine.Debug:LogException(Exception)
Unity.Netcode.NetworkBehaviour:InternalOnNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkBehaviour.cs:910)
Unity.Netcode.NetworkObject:InvokeBehaviourNetworkDespawn() (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkObject.cs:2935)
Unity.Netcode.NetworkSpawnManager:OnDespawnObject(NetworkObject, Boolean, Boolean) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1767)
Unity.Netcode.NetworkSpawnManager:DespawnObject(NetworkObject, Boolean, Boolean) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1441)
Unity.Netcode.NetworkObject:Despawn(Boolean) (at ./Library/PackageCache/com.un…
```

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, Shift, first at 181.3s, seen 3x

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

## StepFailed (3 unique)

### [medium] Input path failed: Take a folder from the stack
Day 1, Shift+Cutscene, first at 48.2s, seen 2x

```
Take a folder from the stack: could not walk to 'Stack Of Folders' (timed out walking).

day=1 phase=Shift+Cutscene campaignComplete=False
shiftStarted=True suspectIndex=0 processed=0 entityAtWindow=True currentSuspect=Suspect_Vlad Variant(Clone) bellReady=False
clockInArmed=False clockOutArmed=False clockedOut=False blockVerdict=True
report=False paused=False dialogueMode=True scripted=True choices=True cutscene=True
canControl=False canInteract=False held=- pos=(-1.2, 0.1, -14.9) timeScale=1 cursorVisible=True

```
Screenshot: `001_StepFailed.png`

### [high] Taking a folder from the stack did not put a folder in hand.
Day 1, Shift+Cutscene, first at 53.3s, seen 2x

```
While handling suspect #0 'Suspect_Vlad Variant(Clone)' (infected=False)

day=1 phase=Shift+Cutscene campaignComplete=False
shiftStarted=True suspectIndex=0 processed=0 entityAtWindow=True currentSuspect=Suspect_Vlad Variant(Clone) bellReady=False
clockInArmed=False clockOutArmed=False clockedOut=False blockVerdict=True
report=False paused=False dialogueMode=True scripted=True choices=False cutscene=True
canControl=False canInteract=False held=- pos=(-1.2, 0.1, -14.9) timeScale=1 cursorVisible=True

```
Screenshot: `002_StepFailed.png`

### [medium] Input path failed: Tutorial: Drawer (1)
Day 1, Shift, first at 99.5s, seen 2x

```
Tutorial: Drawer (1): interaction ray never resolved 'Drawer (1)' (hit: Drawer - folders @ 1.5m).

day=1 phase=Shift campaignComplete=False
shiftStarted=True suspectIndex=0 processed=0 entityAtWindow=True currentSuspect=Suspect_Vlad Variant(Clone) bellReady=False
clockInArmed=False clockOutArmed=False clockedOut=False blockVerdict=True
report=False paused=False dialogueMode=False scripted=False choices=False cutscene=False
canControl=True canInteract=True held=ID card(Clone) pos=(0.7, 0.1, -12.3) timeScale=1 cursorVisible=False
tutorialTargets=Tutorial Arrow - Folders, Tutorial Arrow(Clone)

```
Screenshot: `003_StepFailed.png`

## Assist (2 unique)

### [low] Fell back to logic-level action for: Take a folder from the stack
Day 1, Shift+Cutscene, first at 48.2s, seen 2x

### [low] Fell back to logic-level action for: Tutorial: Drawer (1)
Day 1, Shift, first at 99.5s, seen 2x

## Warning (37 unique)

- (904x, Day 1) Physics.ClosestPoint can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.
- (2x, Day 1) PlayOneShot was called with a null AudioClip.
- (1x, Day 1) BoxCollider does not support negative scale or size. The effective box size has been forced positive and is likely to give unexpected collision geometry. If you absolutely need to use negative scaling you can use the convex MeshCollider. Scene hierarchy path "Mine/Cube.015"
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (1)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (4)' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point' has no valid prefab — skipping.
- (1x, Day 1) [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (21)' has no valid prefab — skipping.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.936001, 0.003230, -12.512001 ) to Target position ( -3.096357, -0.047890, -16.895000 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/tvOS/GameAnalyticsTVOSUnity.m.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/WebGL/GameAnalytics.jspre.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/WSA/x64/GameAnalytics.UWP.dll.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/SamsungTV/sqlite3.c.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/Tizen/libGameAnalytics.a.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/tvOS/GameAnalyticsTVOS.h.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/WSA/ARM/GameAnalytics.UWP.dll.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/UMotionEditor/Plugins/Windows/Editor/x64/UMotionFBX_Win_x64.dll.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/WSA/x86/GameAnalytics.UWP.dll.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/iOS/GameAnalytics.h.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/tvOS/libGameAnalyticsTVOS.a.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/iOS/libGameAnalytics.a.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/WebGL/GameAnalyticsUnity.jslib.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Serialized file "Assets/GameAnalytics/Plugins/SamsungTV/sqlite3.h.meta" contains a PluginImporter object at version 1, below the supported minimum (2). Open and re-save the file to upgrade.
- (1x, Day 1) Parameter 'Talk1' does not exist.
- (1x, Day 1) Parameter 'Talk3' does not exist.
- (1x, Day 1) [Dissonance:Recording] (20:28:16.001) BasicMicrophoneCapture: Insufficient buffer space, requested 17280, clamped to 16383 (dropping 897 samples)
- (1x, Day 1) [Dissonance:Recording] (20:28:16.001) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( -0.766649, 0.003230, -13.273621 ) to Target position ( 0.612801, -0.047890, -12.212223 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 1.149759, 0.003230, -12.278286 ) to Target position ( 0.612801, -0.047890, -12.212223 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.730907, -0.047890, -12.262873 ) to Target position ( -0.694038, 0.003230, -12.331444 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.730923, -0.047890, -12.262825 ) to Target position ( -0.694038, 0.003230, -12.331444 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) [Netcode] [Folder(Clone)][NetworkTransform][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!
- (1x, Day 1) [Netcode] [Folder(Clone)][NetworkRigidbody][isActiveAndEnabled: False] Disabled NetworkBehaviours will be excluded from spawning and synchronization!
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.730915, -0.047890, -12.262825 ) to Target position ( 0.858091, 0.003230, -12.265522 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.730917, -0.047890, -12.262825 ) to Target position ( -0.693317, 0.003230, -12.331354 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) CalculatePath() could not determine precisely which agent type should move from Source position ( 0.730921, -0.047890, -12.262827 ) to Target position ( -0.693317, 0.003230, -12.331354 ). Use the filter parameter of CalculatePath() to specify the agent type.
- (1x, Day 1) [Dissonance:Recording] (20:30:23.522) BasicMicrophoneCapture: Insufficient buffer space, requested 35040, clamped to 16383 (dropping 18657 samples)
- (1x, Day 1) [Dissonance:Recording] (20:30:23.522) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate

## Info (1 unique)

- (1x, Day 1) Tutorial target 'Tutorial Arrow - Pick up docs' could not be actioned

## Timeline

```
[    0.0s] D1 PreShift | Autopilot started — mode=Input days=1..last verdicts=Correct
[    3.0s] D1 PreShift | Authored days in scene: [1, 2, 3, 4, 5] — playing until Day 5.
[    3.0s] D1 PreShift | Virtual gamepad attached.
[    3.0s] D1 PreShift | === Day 1 started ===
[    3.1s] D1 PreShift | Clocking in.
[   12.5s] D1 PreShift | Exception: UnassignedReferenceException: The variable Hips of LegsAnimator has not been assigned.
You probably need to assign the Hips variable of the LegsAnimator script in the inspector.
[   23.2s] D1 Shift | Processing suspect #0 'Suspect_Vlad Variant(Clone)' (infected=False) → Pass.
[   48.2s] D1 Shift+Cutscene | StepFailed: Input path failed: Take a folder from the stack
[   48.2s] D1 Shift+Cutscene | Assist: Fell back to logic-level action for: Take a folder from the stack
[   53.3s] D1 Shift+Cutscene | StepFailed: Taking a folder from the stack did not put a folder in hand.
[   53.3s] D1 Shift+Cutscene | Processing suspect #0 'Suspect_Vlad Variant(Clone)' (infected=False) → Pass.
[   63.8s] D1 Shift | Tutorial arrow → interacting with 'Telephone'.
[   71.8s] D1 Shift | Tutorial arrow → interacting with 'Telephone'.
[   80.0s] D1 Shift | Tutorial arrow → interacting with 'Telephone'.
[   95.0s] D1 Shift | Tutorial arrow → interacting with 'Drawer (1)'.
[   99.5s] D1 Shift | StepFailed: Input path failed: Tutorial: Drawer (1)
[   99.5s] D1 Shift | Assist: Fell back to logic-level action for: Tutorial: Drawer (1)
[  103.0s] D1 Shift | Tutorial arrow → interacting with 'Drawer (1)'.
[  111.3s] D1 Shift | Tutorial arrow → interacting with 'Drawer (1)'.
[  143.2s] D1 Shift | Tutorial arrow → interacting with 'Folder(Clone)'.
[  163.9s] D1 Shift | Tutorial arrow → interacting with 'Drawer - folders'.
[  164.6s] D1 Shift | Exception: NullReferenceException: Object reference not set to an instance of an object
[  171.9s] D1 Shift | Tutorial arrow → interacting with 'Drawer - folders'.
[  181.3s] D1 Shift | Exception: NullReferenceException: Object reference not set to an instance of an object
```
