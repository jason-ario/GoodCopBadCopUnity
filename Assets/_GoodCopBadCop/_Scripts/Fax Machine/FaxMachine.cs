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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        if (IsServer)
        {
            ShiftManager.Instance.OnNightPhaseBegin += SpawnNewspaper;
            ShiftManager.Instance.OnDayStart += SpawnDailyFax;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        if (IsServer)
        {
            ShiftManager.Instance.OnNightPhaseBegin -= SpawnNewspaper;
            ShiftManager.Instance.OnDayStart -= SpawnDailyFax;
        }
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

    private void SpawnDailyFax()
    {
        if (ShiftManager.Instance.CurrentDay <= 1)
        {
            Debug.Log("[FaxMachine] Day 1 — skipping daily fax spawn.");
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
    }

    /// <summary>
    /// Populates the daily fax canvas then ejects a fax paper from the machine.
    /// Falls back to the newspaper spawn point if no dedicated fax spawn point is assigned.
    /// </summary>
    IEnumerator WaitAndSpawnFax()
    {
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
    }
}
