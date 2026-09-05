using System;
using UnityEngine;

/// <summary>
/// Host-authoritative, resumable snapshot of the active campaign day. This state is deliberately
/// separate from permanent campaign progression: it is cleared only after the campaign advances.
/// Dynamic NetworkObjects are reconstructed by the host from this data and then replicated by NGO.
/// </summary>
[Serializable]
public class WorkdaySaveState
{
    public bool IsValid;
    public int Day;
    public int Phase;
    public bool ShiftStarted;
    public bool SuspectsComplete;
    public bool ClockInArmed;
    public bool ClockOutEnabled;
    public bool ClockedOut;
    public int SuspectsProcessed;
    public int SuspectsPassedCorrect;
    public int SuspectsPassedWrong;
    public int SuspectsQuarantined;
    public int SuspectsKilledCorrect;
    public int SuspectsKilledWrong;
    public int SuspectsFled;
    public int SuspectIndex;
    public int Cash;
    public PickableObjectSaveData[] Pickables = Array.Empty<PickableObjectSaveData>();
    public bool DailyPickupsInitialized;
    public DailyPickupSaveData[] DailyPickups = Array.Empty<DailyPickupSaveData>();
    public ProcessResidentsTaskSaveState ProcessResidents = new();
    public GraffitiTaskSaveState Graffiti = new();
    public TrashTaskSaveState Trash = new();
    public BloodTaskSaveState Blood = new();
    public MailTaskSaveState Mail = new();
    public FenceTaskSaveState FenceRepair = new();
    public FollowTrailTaskSaveState FollowTrail = new();
    public BoothMessTaskSaveState BoothMess = new();
    public string[] PendingDailyTaskIds = Array.Empty<string>();
}

[Serializable]
public class DailyPickupSaveData
{
    public int SpawnPointIndex;
    public int PrefabIndex;
    public string SaveId;
    public Vector3 Position;
    public Vector3 RotationEuler;
}

[Serializable]
public class ProcessResidentsTaskSaveState
{
    public bool IsActive;
    public int ProcessedCount;
    public int TotalCount;
}

[Serializable]
public class GraffitiTaskSaveState
{
    public bool IsActive;
    public bool IsComplete;
    public int ScrubbedCount;
    public int TotalCount;
    public GraffitiPlacementSaveData[] Placements = Array.Empty<GraffitiPlacementSaveData>();
}

[Serializable]
public class GraffitiPlacementSaveData
{
    public int PrefabIndex;
    public int SpawnPointIndex;
    public float ScrubProgress;
}

[Serializable]
public class TrashTaskSaveState
{
    public bool IsActive;
    public bool IsGoreTask;
    public int DepositedCount;
    public int TotalCount;
    public int PendingBonusCollected;
    public WorldObjectPlacementSaveData[] Items = Array.Empty<WorldObjectPlacementSaveData>();
    public WorldObjectPlacementSaveData[] BloodDecals = Array.Empty<WorldObjectPlacementSaveData>();
}

[Serializable]
public class BloodTaskSaveState
{
    public bool IsActive;
    public bool IsComplete;
    public int ScrubbedCount;
    public int TotalCount;
}

[Serializable]
public class MailTaskSaveState
{
    public bool IsActive;
    public int SortedCount;
    public int TotalCount;
    public MailPackageSaveData[] Packages = Array.Empty<MailPackageSaveData>();
}

[Serializable]
public class MailPackageSaveData
{
    public int ResidentPoolIndex;
    public string ResidentName;
    public string GoodsLabel;
    public int CorrectBin;
    public Vector3 Position;
    public Vector3 RotationEuler;
}

[Serializable]
public class FenceTaskSaveState
{
    public bool IsActive;
    public bool IsComplete;
    public int[] DamageStates = Array.Empty<int>();
}

[Serializable]
public class FollowTrailTaskSaveState
{
    public bool IsFollowTrailActive;
    public int KillMutantCount;
}

[Serializable]
public class BoothMessTaskSaveState
{
    public bool IsActive;
}

[Serializable]
public class WorldObjectPlacementSaveData
{
    public int PrefabIndex;
    public Vector3 Position;
    public Vector3 RotationEuler;
    public Vector3 LocalScale = Vector3.one;
    public float ScrubProgress;
}
