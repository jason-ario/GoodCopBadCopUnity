using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class FaxMachine : NetworkBehaviour
{
    [SerializeField] Newspaper newspaper;
    [SerializeField] Transform newspaperSpawnPoint;
    [SerializeField] private AudioSource faxAudioSource;
    [SerializeField] AudioClip faxClip;
    [SerializeField] private Animator _animator;
    [SerializeField] private MachineShake _machineShake;

    [Header("Daily Fax")]
    [SerializeField] private DailyFaxContentsController _dailyFaxContents;
    [SerializeField] private Newspaper _faxPaper;
    [SerializeField] private Transform _faxPaperSpawnPoint;

    [Header("Tutorial")]
    [Tooltip("World-space arrow that points at the fax machine while a delivered fax is waiting to be picked up.")]
    [SerializeField] private GameObject _tutorialArrow;

    // The fax paper spawned for the current day, if any, so it can be despawned next day
    // (mirrors DailyNewspaperSpawnManager's _activeNewspaper tracking).
    private NetworkObject _activeFax;

    // The PickableObject on the active fax, so its pickup events can be unsubscribed.
    private PickableObject _activeFaxPickable;

    // True once the player has ever picked up a daily fax — guards ShowDailyFaxTutorial so it
    // only ever plays the first time, not every subsequent day's fax pickup.
    private bool _hasShownDailyFaxTutorial;

    public static FaxMachine Instance { get; private set; }

    /// <summary>
    /// Server-only. When set to the current day, the clock-in punch does NOT deliver the daily
    /// fax; it's held until <see cref="ReleaseDeferredDailyFax"/> is called (e.g. Day 4 waits for
    /// its new-voice megaphone announcement to finish). Mirrors
    /// <see cref="SortMailTask.DeferAutoTriggerForDay"/>. -1 = no deferral.
    /// </summary>
    public static int DeferDailyFaxForDay = -1;

    // True once a clock-in fax delivery was swallowed by DeferDailyFaxForDay and is waiting
    // on ReleaseDeferredDailyFax.
    private bool _dailyFaxPending;

    private void Awake() => Instance = this;

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
    }

    /// <summary>
    /// Server-only. Releases a daily fax deferred via <see cref="DeferDailyFaxForDay"/>. If the
    /// clock-in hasn't happened yet, the deferral is simply cleared so the normal clock-in
    /// delivery runs; otherwise the held fax is delivered now.
    /// </summary>
    public static void ReleaseDeferredDailyFax(int day)
    {
        if (DeferDailyFaxForDay == day)
        {
            DeferDailyFaxForDay = -1;
            return;
        }
        Instance?.TriggerDeferredDailyFax();
    }

    private void TriggerDeferredDailyFax()
    {
        if (this == null || !IsServer || !_dailyFaxPending) return;
        _dailyFaxPending = false;
        Debug.Log("[FaxMachine] Releasing deferred daily fax.");
        StartCoroutine(WaitAndSpawnFax());
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            ShiftManager.Instance.OnNightPhaseBegin += SpawnNewspaper;
            // The daily fax arrives once the team clocks in, not at day start.
            TimecardMachine.OnClockInServer += SpawnDailyFax;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer)
        {
            if (ShiftManager.Instance != null)
                ShiftManager.Instance.OnNightPhaseBegin -= SpawnNewspaper;
            TimecardMachine.OnClockInServer -= SpawnDailyFax;
        }

        if (_activeFaxPickable != null)
            _activeFaxPickable.OnPickedUpEvent -= HandleFaxPickedUpFirstTime;
    }

    private void SpawnNewspaper()
    {
        if (ShiftManager.Instance.CurrentDay <= 1)
        {
            Debug.Log("[FaxMachine] Day 1 — skipping newspaper spawn.");
            return;
        }
        StartCoroutine(WaitAndSpawnNewspaper());
    }

    /// <summary>
    /// Server-only. Fired by <see cref="TimecardMachine.OnClockInServer"/> the moment a clock-in
    /// punch is accepted (only possible during PreShift, once per day).
    /// </summary>
    private void SpawnDailyFax()
    {
        if (this == null || !IsServer || ShiftManager.Instance == null) return;

        if (ShiftManager.Instance.CurrentDay <= 1)
        {
            Debug.Log("[FaxMachine] Day 1 — skipping daily fax spawn.");
            return;
        }

        if (ShiftManager.Instance.CurrentDay == DeferDailyFaxForDay)
        {
            DeferDailyFaxForDay = -1;
            _dailyFaxPending = true;
            Debug.Log($"[FaxMachine] Day {ShiftManager.Instance.CurrentDay} daily fax deferred — waiting for ReleaseDeferredDailyFax().");
            return;
        }

        StartCoroutine(WaitAndSpawnFax());
    }

    IEnumerator WaitAndSpawnNewspaper()
    {
        yield return new WaitForSeconds(5);

        GameObject spawnedNewspaper = Instantiate(newspaper.gameObject, newspaperSpawnPoint.position, newspaperSpawnPoint.rotation);

        NetworkObject networkObject = spawnedNewspaper.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("[FaxMachine] Newspaper prefab is missing NetworkObject component!");
            Destroy(spawnedNewspaper);
            yield break;
        }

        _machineShake.enabled = true;
        faxAudioSource.PlayOneShot(faxClip);
        yield return new WaitForSeconds(4);

        networkObject.Spawn(true);
        PickableObject pickable = networkObject.GetComponent<PickableObject>();
        pickable.SetParent(newspaperSpawnPoint);
        pickable.LockInteractableNetworked();

        _animator.enabled = true;
        yield return new WaitForSeconds(2);
        _machineShake.enabled = false;

        pickable.UnlockInteractableNetworked();

        ShowTutorialArrowUntilPickedUp(pickable);
    }

    /// <summary>
    /// Populates the daily fax canvas then ejects a fax paper from the machine.
    /// Falls back to the newspaper spawn point if no dedicated fax spawn point is assigned.
    /// Despawns the previous day's fax paper first (if still present), so at most one daily
    /// fax exists in the scene at a time — mirrors <see cref="DailyNewspaperSpawnManager"/>.
    /// </summary>
    IEnumerator WaitAndSpawnFax()
    {
        DespawnPreviousFax();

        if (_dailyFaxContents == null)
        {
            Debug.LogWarning("[FaxMachine] No DailyFaxContentsController assigned — skipping daily fax.");
            yield break;
        }

        // Populate the fax canvas so the render texture is ready.
        _dailyFaxContents.PopulateFaxContents();

        // Let the camera snapshot complete before the paper appears.
        yield return new WaitForSeconds(2);

        Newspaper paperPrefab = _faxPaper != null ? _faxPaper : newspaper;
        Transform spawnPoint = _faxPaperSpawnPoint != null ? _faxPaperSpawnPoint : newspaperSpawnPoint;

        if (paperPrefab == null)
        {
            Debug.LogWarning("[FaxMachine] No fax paper prefab assigned — daily fax will not eject a paper.");
            yield break;
        }

        GameObject spawnedPaper = Instantiate(paperPrefab.gameObject, spawnPoint.position, spawnPoint.rotation);

        NetworkObject networkObject = spawnedPaper.GetComponent<NetworkObject>();
        if (networkObject == null)
        {
            Debug.LogError("[FaxMachine] Fax paper prefab is missing NetworkObject component!");
            Destroy(spawnedPaper);
            yield break;
        }

        _machineShake.enabled = true;
        faxAudioSource.PlayOneShot(faxClip);
        yield return new WaitForSeconds(4);

        networkObject.Spawn(true);
        PickableObject pickable = networkObject.GetComponent<PickableObject>();
        pickable.SetParent(spawnPoint);
        pickable.LockInteractableNetworked();

        _animator.enabled = true;
        yield return new WaitForSeconds(2);
        _machineShake.enabled = false;

        pickable.UnlockInteractableNetworked();

        // The eject animation is done: drop the constraint and freeze the paper in place
        // (kinematic + trigger) so it neither falls nor fights the surface it overlaps.
        pickable.ReleaseConstraintInPlaceNetworked();

        _activeFax = networkObject;
        _activeFaxPickable = pickable;

        ShowFaxTutorial(pickable);
    }

    /// <summary>
    /// Despawns the previous day's fax paper if it's still sitting unpicked in the scene.
    /// Clears its arrow and unsubscribes before the instance is despawned, mirroring
    /// <see cref="DailyNewspaperSpawnManager.DespawnPreviousNewspaper"/>.
    /// </summary>
    private void DespawnPreviousFax()
    {
        if (_activeFaxPickable != null)
        {
            _activeFaxPickable.OnPickedUpEvent -= HandleFaxPickedUpFirstTime;
        }
        _activeFaxPickable = null;

        if (_tutorialArrow != null)
            _tutorialArrow.SetActive(false);

        if (_activeFax != null && _activeFax.IsSpawned)
            _activeFax.Despawn();

        _activeFax = null;
    }

    /// <summary>
    /// Points the tutorial arrow at the freshly delivered fax until it's picked up. The very
    /// first time any fax is ever grabbed, also shows the "Daily Fax" tutorial overlay
    /// explaining what the fax is for.
    /// </summary>
    private void ShowFaxTutorial(PickableObject pickable)
    {
        if (pickable == null) return;

        ShowTutorialArrowUntilPickedUp(pickable);

        pickable.OnPickedUpEvent += HandleFaxPickedUpFirstTime;
    }

    private void HandleFaxPickedUpFirstTime()
    {
        if (_activeFaxPickable != null)
        {
            _activeFaxPickable.OnPickedUpEvent -= HandleFaxPickedUpFirstTime;
        }

        if (!_hasShownDailyFaxTutorial)
        {
            _hasShownDailyFaxTutorial = true;
            TutorialOverlay.Instance?.ShowDailyFaxTutorial();
        }
    }

    /// <summary>
    /// Shows the fax machine tutorial arrow and automatically hides it the moment
    /// <paramref name="pickable"/> is grabbed. Safe to call even if no arrow is assigned.
    /// </summary>
    private void ShowTutorialArrowUntilPickedUp(PickableObject pickable)
    {
        if (_tutorialArrow == null || pickable == null) return;

        _tutorialArrow.SetActive(true);

        void OnPickedUp()
        {
            _tutorialArrow.SetActive(false);
            pickable.OnPickedUpEvent -= OnPickedUp;
        }

        pickable.OnPickedUpEvent += OnPickedUp;
    }
}
