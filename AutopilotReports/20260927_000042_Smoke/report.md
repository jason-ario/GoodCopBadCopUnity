# Autopilot report — FAIL

- Mode: `Smoke`  Label: `bezi`
- Started: 2026-09-27T00:00:42  Duration: 57s
- End reason: Reached the configured end (Day 1 completed).

| Exceptions | Errors | Stalls | Step failures | Invariants | Assists | Warnings |
|---|---|---|---|---|---|---|
| 1 | 4 | 0 | 1 | 0 | 0 | 22097 |

## Days

| Day | Outcome | Duration | Suspects | Errors | Stalls | Step failures | Assists |
|---|---|---|---|---|---|---|---|
| 1 | completed | 45s | 0 | 3 | 0 | 1 | 0 |

## Exception (1 unique)

### [high] NullReferenceException: Object reference not set to an instance of an object
Day 1, PreShift, first at 16.7s, seen 1x

```
Day_01+<Day1OpeningSequence>d__162.MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/Days/Day_01.cs:1090)
UnityEngine.SetupCoroutine.InvokeMoveNext (System.Collections.IEnumerator enumerator, System.IntPtr returnValueAddress) (at <d5ea965f39d74501b56f4006ef80da34>:0)
UnityEngine.StackTraceUtility:ExtractStringFromExceptionInternal(Object, String&, String&)

```

## Error (1 unique)

### [high] GameAnalytics: REMEMBER THE SDK NEEDS TO BE MANUALLY INITIALIZED NOW
Day 1, PreShift, first at 5.0s, seen 4x

```
UnityEngine.Debug:LogError (object)
GameAnalyticsSDK.GameAnalytics:NewDesignEvent (string) (at Assets/GameAnalytics/Plugins/Scripts/GameAnalytics.cs:459)
CampaignManager:ApplyDay (int) (at Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs:411)
CampaignManager:JumpToDay (int) (at Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs:250)
DebugConsole:SkipToDay (int) (at Assets/_GoodCopBadCop/_Scripts/DebugConsole/DebugConsole.cs:471)
DebugConsole:<ApplyEditorDebugStartPoint>b__27_0 () (at Assets/_GoodCopBadCop/_Scripts/DebugConsole/DebugConsole.cs:344)
DebugConsole/<EnsureGameStartedThenWaitForPlayer>d__26:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/DebugConsole/DebugConsole.cs:329)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

## StepFailed (1 unique)

### [high] Shift did not start in smoke mode.
Day 1, PreShift, first at 19.7s, seen 1x

```
day=1 phase=PreShift campaignComplete=False
shiftStarted=False suspectIndex=-1 processed=0 entityAtWindow=False currentSuspect=- bellReady=False
clockInArmed=False clockOutArmed=False clockedOut=False blockVerdict=True
report=False paused=False dialogueMode=False scripted=False choices=False cutscene=False
canControl=True canInteract=True held=- pos=(1.2, 0.1, -12.9) timeScale=1 cursorVisible=False

```
Screenshot: `001_StepFailed.png`

## Warning (60 unique)

### [low] BoxCollider does not support negative scale or size.
The effective box size has been forced positive and is likely to give unexpected collision geometry.
If you absolutely need to use negative scaling you can use the convex MeshCollider. Scene hierarchy path "Mine/Cube.015"
Day 1, PreShift, first at 0.4s, seen 1x

### [low] Animator is not playing an AnimatorController
Day 1, PreShift, first at 0.4s, seen 16065x

```
UnityEngine.Animator:IsParameterControlledByCurve (int)
Unity.Netcode.Components.NetworkAnimator:CheckParametersChanged () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1277)
Unity.Netcode.Components.NetworkAnimator:CheckForAnimatorChanges () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1153)
Unity.Netcode.Components.NetworkAnimatorStateChangeHandler:NetworkUpdate (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:119)
Unity.Netcode.NetworkUpdateLoop:RunNetworkUpdateStage (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:191)
Unity.Netcode.NetworkUpdateLoop/NetworkPreUpdate/<>c:<CreateLoopSystem>b__0_0 () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:238)

```

### [low] Animator is not playing an AnimatorController
Day 1, PreShift, first at 0.4s, seen 5049x

```
UnityEngine.Animator:GetBool (int)
Unity.Netcode.Components.NetworkAnimator:CheckParametersChanged () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1294)
Unity.Netcode.Components.NetworkAnimator:CheckForAnimatorChanges () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1153)
Unity.Netcode.Components.NetworkAnimatorStateChangeHandler:NetworkUpdate (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:119)
Unity.Netcode.NetworkUpdateLoop:RunNetworkUpdateStage (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:191)
Unity.Netcode.NetworkUpdateLoop/NetworkPreUpdate/<>c:<CreateLoopSystem>b__0_0 () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:238)

```

### [low] Animator is not playing an AnimatorController
Day 1, PreShift, first at 0.4s, seen 459x

```
UnityEngine.Animator:GetInteger (int)
Unity.Netcode.Components.NetworkAnimator:CheckParametersChanged () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1284)
Unity.Netcode.Components.NetworkAnimator:CheckForAnimatorChanges () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1153)
Unity.Netcode.Components.NetworkAnimatorStateChangeHandler:NetworkUpdate (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:119)
Unity.Netcode.NetworkUpdateLoop:RunNetworkUpdateStage (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:191)
Unity.Netcode.NetworkUpdateLoop/NetworkPreUpdate/<>c:<CreateLoopSystem>b__0_0 () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:238)

```

### [low] Animator is not playing an AnimatorController
Day 1, PreShift, first at 0.4s, seen 459x

```
UnityEngine.Animator:get_layerCount ()
Unity.Netcode.Components.NetworkAnimator:CheckForAnimatorChanges () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:1171)
Unity.Netcode.Components.NetworkAnimatorStateChangeHandler:NetworkUpdate (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Components/NetworkAnimator.cs:119)
Unity.Netcode.NetworkUpdateLoop:RunNetworkUpdateStage (Unity.Netcode.NetworkUpdateStage) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:191)
Unity.Netcode.NetworkUpdateLoop/NetworkPreUpdate/<>c:<CreateLoopSystem>b__0_0 () (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkUpdateLoop.cs:238)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point' has no valid prefab — skipping.
Day 1, PreShift, first at 5.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<SkipToBoothReadySequence>d__229:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:2190)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (6)' has no valid prefab — skipping.
Day 1, PreShift, first at 5.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<SkipToBoothReadySequence>d__229:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:2190)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (29)' has no valid prefab — skipping.
Day 1, PreShift, first at 5.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<SkipToBoothReadySequence>d__229:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:2190)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (3)' has no valid prefab — skipping.
Day 1, PreShift, first at 5.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<SkipToBoothReadySequence>d__229:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:2190)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (22)' has no valid prefab — skipping.
Day 1, PreShift, first at 5.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<SkipToBoothReadySequence>d__229:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:2190)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (11)' has no valid prefab — skipping.
Day 1, PreShift, first at 5.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<SkipToBoothReadySequence>d__229:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:2190)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [Dissonance:Recording] (20:00:48.510) CapturePipelineManager: Detected a frame skip, forcing capture pipeline reset (Delta Time:0.3096878)
Day 1, PreShift, first at 5.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:Warn (string) (at Assets/Plugins/Dissonance/Core/Log.cs:377)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:195)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] PlayOneShot was called with a null AudioClip.
Day 1, PreShift, first at 6.0s, seen 7x

```
UnityEngine.AudioSource:PlayOneShot (UnityEngine.AudioClip)
PlaySoundAnimationEvent:PlaySound (UnityEngine.AudioClip) (at Assets/_GoodCopBadCop/_Scripts/Helper/PlaySoundAnimationEvent.cs:11)

```

### [low] [Dissonance:Recording] (20:00:55.764) CapturePipelineManager: Detected a frame skip, forcing capture pipeline reset (Delta Time:0.15049)
Day 1, PreShift, first at 12.9s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:Warn (string) (at Assets/Plugins/Dissonance/Core/Log.cs:377)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:195)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:00:57.730) CapturePipelineManager: Detected a frame skip, forcing capture pipeline reset (Delta Time:0.1778512)
Day 1, PreShift, first at 14.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:Warn (string) (at Assets/Plugins/Dissonance/Core/Log.cs:377)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:195)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:01.957) CapturePipelineManager: Detected a frame skip, forcing capture pipeline reset (Delta Time:0.2186003)
Day 1, PreShift, first at 19.0s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:Warn (string) (at Assets/Plugins/Dissonance/Core/Log.cs:377)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:195)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:03.006) BasicMicrophoneCapture: Insufficient buffer space, requested 18240, clamped to 16383 (dropping 1857 samples)
Day 1, PreShift, first at 20.1s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:03.007) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 20.1s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] Physics.ClosestPoint can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.
Day 1, PreShift, first at 21.2s, seen 2x

```
UnityEngine.Collider:ClosestPoint (UnityEngine.Vector3)
PlayerInteractionController:FindNearbyPlacementBoard (UnityEngine.Vector3) (at Assets/_GoodCopBadCop/_Scripts/Interaction System/PlayerInteractionController.cs:676)
PlayerInteractionController:HandleReticle () (at Assets/_GoodCopBadCop/_Scripts/Interaction System/PlayerInteractionController.cs:300)
PlayerInteractionController:Update () (at Assets/_GoodCopBadCop/_Scripts/Interaction System/PlayerInteractionController.cs:142)

```

### [low] [Dissonance:Recording] (20:01:09.547) BasicMicrophoneCapture: Insufficient buffer space, requested 18240, clamped to 16383 (dropping 1857 samples)
Day 1, PreShift, first at 26.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:09.547) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 26.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:22.992) BasicMicrophoneCapture: Insufficient buffer space, requested 140160, clamped to 16383 (dropping 123777 samples)
Day 1, PreShift, first at 40.1s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:22.993) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 40.1s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:23.214) CapturePipelineManager: Detected a frame skip, forcing capture pipeline reset (Delta Time:0.2133759)
Day 1, PreShift, first at 40.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:Warn (string) (at Assets/Plugins/Dissonance/Core/Log.cs:377)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:195)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:24.231) BasicMicrophoneCapture: Insufficient buffer space, requested 17760, clamped to 16383 (dropping 1377 samples)
Day 1, PreShift, first at 41.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:24.231) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 41.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:24.925) BasicMicrophoneCapture: Insufficient buffer space, requested 26880, clamped to 16383 (dropping 10497 samples)
Day 1, PreShift, first at 42.0s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:24.925) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 42.0s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:25.496) BasicMicrophoneCapture: Insufficient buffer space, requested 21120, clamped to 16383 (dropping 4737 samples)
Day 1, PreShift, first at 42.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:25.497) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 42.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:26.343) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 43.4s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:26.780) BasicMicrophoneCapture: Insufficient buffer space, requested 16800, clamped to 16383 (dropping 417 samples)
Day 1, PreShift, first at 43.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:26.781) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 43.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:27.762) BasicMicrophoneCapture: Insufficient buffer space, requested 22560, clamped to 16383 (dropping 6177 samples)
Day 1, PreShift, first at 44.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:27.762) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 44.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:28.266) BasicMicrophoneCapture: Insufficient buffer space, requested 16800, clamped to 16383 (dropping 417 samples)
Day 1, PreShift, first at 45.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:28.267) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 1, PreShift, first at 45.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Netcode] Destroying in-scene network objects can lead to unexpected behavior. It is recommended to use NetworkObject.Despawn(false) instead.
Day 2, Report, first at 46.3s, seen 1x

```
UnityEngine.Logger:Log (UnityEngine.LogType,object,UnityEngine.Object)
Unity.Netcode.Logging.ContextualLogger:Log (UnityEngine.LogType,Unity.Netcode.Logging.Context)
Unity.Netcode.Logging.ContextualLogger:Warning (Unity.Netcode.Logging.Context)
Unity.Netcode.NetworkLog:LogWarning (string)
Unity.Netcode.NetworkSpawnManager:OnDespawnObject (Unity.Netcode.NetworkObject,bool,bool) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1720)
Unity.Netcode.NetworkSpawnManager:DespawnObject (Unity.Netcode.NetworkObject,bool,bool) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Spawning/NetworkSpawnManager.cs:1441)
Unity.Netcode.NetworkObject:Despawn (bool) (at ./Library/PackageCache/com.unity.netcode.gameobjects@cfd429cc91ec/Runtime/Core/NetworkObject.cs:2127)
CampaignManager:DespawnDay1Soldier () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs:449)
CampaignManager:ApplyDay (int) (at Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs:422)
CampaignManager:AdvanceDay () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/CampaignManager.cs:304)
ShiftManager:TriggerEndOfShiftReport…
```

### [low] [Dissonance:Recording] (20:01:29.286) BasicMicrophoneCapture: Insufficient buffer space, requested 21120, clamped to 16383 (dropping 4737 samples)
Day 2, Report, first at 46.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:29.286) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 46.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:30.156) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 47.2s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:30.678) BasicMicrophoneCapture: Insufficient buffer space, requested 19680, clamped to 16383 (dropping 3297 samples)
Day 2, Report, first at 47.7s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:30.679) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 47.7s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:30.679) BasePreprocessingPipeline: Lost 960 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 47.7s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:32.732) BasicMicrophoneCapture: Insufficient buffer space, requested 16800, clamped to 16383 (dropping 417 samples)
Day 2, Report, first at 49.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:32.732) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 49.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:33.290) BasicMicrophoneCapture: Insufficient buffer space, requested 21120, clamped to 16383 (dropping 4737 samples)
Day 2, Report, first at 50.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:33.290) BasePreprocessingPipeline: Lost 960 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 50.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:33.761) CapturePipelineManager: Detected a frame skip, forcing capture pipeline reset (Delta Time:0.323366)
Day 2, Report, first at 50.8s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:Warn (string) (at Assets/Plugins/Dissonance/Core/Log.cs:377)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:195)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:34.241) BasicMicrophoneCapture: Insufficient buffer space, requested 17760, clamped to 16383 (dropping 1377 samples)
Day 2, Report, first at 51.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:34.241) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, Report, first at 51.3s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (15)' has no valid prefab — skipping.
Day 2, PreShift, first at 53.9s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<InBetweenShiftSequence>d__192:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:1402)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (25)' has no valid prefab — skipping.
Day 2, PreShift, first at 53.9s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<InBetweenShiftSequence>d__192:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:1402)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (23)' has no valid prefab — skipping.
Day 2, PreShift, first at 53.9s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<InBetweenShiftSequence>d__192:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:1402)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [DailyPickupSpawnManager] Spawn point 'Pickup Spawn Point (5)' has no valid prefab — skipping.
Day 2, PreShift, first at 53.9s, seen 1x

```
UnityEngine.Debug:LogWarning (object,UnityEngine.Object)
DailyPickupSpawnManager:SpawnFreshPickups () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:162)
DailyPickupSpawnManager:OnDayStart () (at Assets/_GoodCopBadCop/_Scripts/DailyPickupSpawnManager.cs:100)
ShiftManager/<InBetweenShiftSequence>d__192:MoveNext () (at Assets/_GoodCopBadCop/_Scripts/Game Systems/ShiftManager.cs:1402)
UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)

```

### [low] [Dissonance:Recording] (20:01:37.379) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, PreShift, first at 54.4s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:39.075) BasicMicrophoneCapture: Insufficient buffer space, requested 19200, clamped to 16383 (dropping 2817 samples)
Day 2, PreShift, first at 56.1s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:39.075) BasePreprocessingPipeline: Lost 480 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, PreShift, first at 56.1s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

### [low] [Dissonance:Recording] (20:01:39.563) BasicMicrophoneCapture: Insufficient buffer space, requested 17760, clamped to 16383 (dropping 1377 samples)
Day 2, PreShift, first at 56.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<uint, uint, single> (Dissonance.LogLevel,string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:198)
Dissonance.Log:Warn<uint, uint, single> (string,uint,uint,single) (at Assets/Plugins/Dissonance/Core/Log.cs:395)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:323)
Dissonance.Audio.Capture.BasicMicrophoneCapture:UpdateSubscribers () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:283)
Dissonance.Audio.Capture.CapturePipelineManager:Update (bool,single) (at Assets/Plugins/Dissonance/Core/Audio/Capture/CapturePipelineManager.cs:181)
Dissonance.DissonanceComms:Update () (at Assets/Plugins/Dissonance/Core/DissonanceCommsImpl.cs:675)

```

### [low] [Dissonance:Recording] (20:01:39.565) BasePreprocessingPipeline: Lost 960 samples in the preprocessor (buffer full), injecting silence to compensate
Day 2, PreShift, first at 56.6s, seen 1x

```
UnityEngine.Debug:LogWarning (object)
Dissonance.Logs/LogMessage:Log () (at Assets/Plugins/Dissonance/Core/Log.cs:68)
Dissonance.Logs:SendLogMessage (string,Dissonance.LogLevel) (at Assets/Plugins/Dissonance/Core/Log.cs:98)
Dissonance.Log:WriteLog (Dissonance.LogLevel,string) (at Assets/Plugins/Dissonance/Core/Log.cs:171)
Dissonance.Log:WriteLogFormat<int> (Dissonance.LogLevel,string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:180)
Dissonance.Log:Warn<int> (string,int) (at Assets/Plugins/Dissonance/Core/Log.cs:383)
Dissonance.Audio.Capture.BasePreprocessingPipeline:Dissonance.Audio.Capture.IMicrophoneSubscriber.ReceiveMicrophoneData (System.ArraySegment`1<single>,NAudio.Wave.WaveFormat) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasePreprocessingPipeline.cs:192)
Dissonance.Audio.Capture.BasicMicrophoneCapture:SendFrame () (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:422)
Dissonance.Audio.Capture.BasicMicrophoneCapture:ConsumeSamples (System.ArraySegment`1<single>) (at Assets/Plugins/Dissonance/Core/Audio/Capture/BasicMicrophoneCapture.cs:380)
Dissonance.Audio.Capture.BasicMicrophoneCapture:DrainMicSamples () (at Assets/Plugins/Dissonance/Core/…
```

## Timeline

```
[    0.0s] D1 PreShift | Autopilot started — mode=Smoke days=1..1 verdicts=Correct
[    5.0s] D1 PreShift | Error: GameAnalytics: REMEMBER THE SDK NEEDS TO BE MANUALLY INITIALIZED NOW
[    6.5s] D1 PreShift | Authored days in scene: [1, 2, 3, 4, 5] — playing until Day 1.
[    6.5s] D1 PreShift | === Day 1 started ===
[    9.6s] D1 PreShift | Smoke: clocking in.
[   16.7s] D1 PreShift | Exception: NullReferenceException: Object reference not set to an instance of an object
[   19.7s] D1 PreShift | StepFailed: Shift did not start in smoke mode.
[   44.8s] D1 PreShift | Smoke: forcing end of Day 1.
[   46.4s] D2 Report | End-of-shift report shown (processed=0).
[   50.4s] D2 Report | Pressing Continue on the report.
```
