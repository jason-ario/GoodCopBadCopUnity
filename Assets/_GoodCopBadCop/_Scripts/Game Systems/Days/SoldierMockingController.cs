using System.Collections;
using DG.Tweening;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Manages the scripted Day 1 soldier event. When <see cref="BeginSequence"/> is invoked
/// via <see cref="SuspectController.InterceptNextSuspectSpawn"/>, the soldier walks to the
/// booth window, delivers a series of mocking lines to the player, then leaves without
/// handing over any paperwork and without being interactable. Fires
/// <see cref="OnSoldierSequenceComplete"/> on the server when the sequence is done so
/// Day_01 can react (e.g. advance tutorial state).
/// </summary>
public class SoldierMockingController : NetworkBehaviour
{
    public static SoldierMockingController Instance { get; private set; }

    /// <summary>Fired on the server once the soldier finishes mocking and begins walking away.</summary>
    public static event System.Action OnSoldierSequenceComplete;

    [Header("Soldier")]
    [Tooltip("The Suspect_Soldier SuspectCharacter prefab to spawn for this scripted event.")]
    [SerializeField] private SuspectCharacter _soldierPrefab;

    [Header("Positions")]
    [Tooltip("Off-screen spawn position the soldier walks in from.")]
    [SerializeField] private Transform _spawnPos;

    [Tooltip("Booth window stand position the soldier walks to.")]
    [SerializeField] private Transform _standPos;

    [Tooltip("Gate position the soldier rotates toward when leaving.")]
    [SerializeField] private Transform _gatePos;

    [Tooltip("Off-screen despawn position the soldier walks to after mocking.")]
    [SerializeField] private Transform _despawnPos;

    [Header("Dialogue")]
    [Tooltip("ScriptedDialogue asset played through ScriptedDialogueRunner when the soldier arrives.")]
    [SerializeField] private ScriptedDialogue _soldierDialogue;

    [Header("Timing")]
    [Tooltip("Seconds to pause after the dialogue finishes before the soldier turns and leaves.")]
    [SerializeField] private float _postLingerSeconds = 2f;

    private SuspectCharacter _spawnedSoldier;

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Begins the scripted soldier mocking sequence. Must be called on the server.
    /// Spawns the soldier, walks him to the booth window, delivers mocking lines without
    /// giving paperwork or allowing interaction, then walks him off-screen.
    /// </summary>
    public void BeginSequence()
    {
        if (!IsServer) return;
        StartCoroutine(SoldierMockingSequence());
    }

    private IEnumerator SoldierMockingSequence()
    {
        // Spawn the soldier character network object.
        GameObject soldierGO = Instantiate(_soldierPrefab.gameObject, _spawnPos.position, _spawnPos.rotation);
        NetworkObject netObj = soldierGO.GetComponent<NetworkObject>();
        netObj.Spawn();

        _spawnedSoldier = soldierGO.GetComponent<SuspectCharacter>();

        // Disable interaction on all clients immediately after spawn.
        DisableInteractionClientRpc(netObj.NetworkObjectId);

        // Walk to the booth window stand position.
        _spawnedSoldier.animator.SetBool("Walking", true);
        _spawnedSoldier.transform
            .DOMove(_standPos.position + _spawnedSoldier.standPosOffset, 3f);
        yield return new WaitForSeconds(3f);

        _spawnedSoldier.animator.SetBool("Walking", false);

        // Rotate to face the booth window.
        _spawnedSoldier.transform.DORotateQuaternion(_standPos.rotation, 0.5f);
        yield return new WaitForSeconds(1f);

        // Play the scripted dialogue through ScriptedDialogueRunner so camera cuts,
        // player advancement, and choice UI all work correctly.
        bool dialogueDone = false;
        ScriptedDialogueRunner.Instance.PlayDialogue(
            _spawnedSoldier, _soldierDialogue, () => dialogueDone = true);
        yield return new WaitUntil(() => dialogueDone);

        yield return new WaitForSeconds(_postLingerSeconds);

        if (_spawnedSoldier == null) yield break;

        // Turn toward the exit gate and walk off-screen.
        _spawnedSoldier.transform.DORotate(_gatePos.rotation.eulerAngles, 0.5f);
        yield return new WaitForSeconds(0.5f);

        _spawnedSoldier.animator.SetBool("Walking", true);
        _spawnedSoldier.transform
            .DOMove(_despawnPos.position, 8f)
            .OnComplete(DespawnSoldier);

        // Signal sequence completion — soldier is the last Day 1 slot, so this triggers clock-out.
        yield return new WaitForSeconds(2f);
        ShiftManager.Instance?.SetNextSuspectReady();
        OnSoldierSequenceComplete?.Invoke();
    }

    private void DespawnSoldier()
    {
        if (!IsServer || _spawnedSoldier == null) return;

        NetworkObject netObj = _spawnedSoldier.GetComponent<NetworkObject>();
        if (netObj != null && netObj.IsSpawned)
            netObj.Despawn(true);

        _spawnedSoldier = null;
    }

    /// <summary>
    /// Disables the soldier's interaction collider on all clients so the player cannot
    /// initiate any dialogue or interaction with him.
    /// </summary>
    [ClientRpc]
    private void DisableInteractionClientRpc(ulong networkObjectId)
    {
        StartCoroutine(WaitAndDisableInteraction(networkObjectId));
    }

    private IEnumerator WaitAndDisableInteraction(ulong networkObjectId)
    {
        // Wait for the NetworkObject to be registered on this client before disabling it.
        while (NetworkManager.Singleton == null ||
               NetworkManager.Singleton.SpawnManager == null ||
               !NetworkManager.Singleton.SpawnManager.SpawnedObjects.ContainsKey(networkObjectId))
        {
            yield return null;
        }

        NetworkObject netObj = NetworkManager.Singleton.SpawnManager.SpawnedObjects[networkObjectId];
        SuspectCharacter character = netObj.GetComponent<SuspectCharacter>();
        character?.SetCanInteract(false);
    }
}
