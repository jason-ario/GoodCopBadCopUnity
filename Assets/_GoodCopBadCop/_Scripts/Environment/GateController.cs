using System.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshObstacle))]
public class GateController : Interactable, IMutantPassable, IHeldItemPassthrough, ILockable
{
    private NetworkVariable<bool> _gateOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> _openedIn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private NetworkVariable<bool> _isLocked = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    [SerializeField] private Animator _animator;
    private bool _beingInteractedWith = false;
    [SerializeField] private float waitDelay = .5f;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip doorOpenClip;
    [SerializeField] private AudioClip doorCloseClip;
    [SerializeField] private AudioClip lockedSound;
    [SerializeField] private Transform forwardMarker;

    [Tooltip("The LockController padlock on this gate. Animated alongside the gate when locked.")]
    [SerializeField] private LockController _lockController;

    private const string AnimLockedShakeParam = "LockedTriedOpening";

    private NavMeshObstacle _navMeshObstacle;

    [Header("Suspect Interaction")]
    [Tooltip("When enabled, the gate automatically opens when a suspect's collider enters the trigger radius.")]
    [SerializeField] private bool _autoOpenForSuspects = true;

    [Tooltip("Physical proximity radius (world units). Gate opens when a suspect collider is within this distance.")]
    [SerializeField] private float _suspectOpenRadius = 0.5f;

    [Tooltip("NavMesh approach radius (world units). Gate opens when a suspect's NavMeshAgent is navigating " +
             "toward the gate and within this distance — catches suspects blocked by the closed NavMeshObstacle.")]
    [SerializeField] private float _suspectNavApproachRadius = 4f;

    private static readonly Collider[] _suspectOverlapBuffer = new Collider[4];

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _navMeshObstacle = GetComponent<NavMeshObstacle>();
        _gateOpen.OnValueChanged  += OnGateStateChanged;
        _openedIn.OnValueChanged  += OnOpenDirectionChanged;
        _isLocked.OnValueChanged  += OnIsLockedChanged;

        // Sync visual state on late join.
        ApplyGateVisuals(_gateOpen.Value, _openedIn.Value);
    }

    private void Update()
    {
        if (!_autoOpenForSuspects) return;
        if (_gateOpen.Value || _isLocked.Value) return;
        if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer) return;

        // Physical proximity — works when the agent can get physically close to the gate.
        int count = Physics.OverlapSphereNonAlloc(transform.position, _suspectOpenRadius, _suspectOverlapBuffer);
        for (int i = 0; i < count; i++)
        {
            if (_suspectOverlapBuffer[i] != null && _suspectOverlapBuffer[i].TryGetComponent<SuspectCharacter>(out var proxSuspect))
            {
                OpenForSuspect(proxSuspect);
                return;
            }
        }

        // NavMesh approach — catches suspects whose path is blocked by the closed NavMeshObstacle.
        // Opens the gate as soon as their agent is navigating toward it within the approach radius,
        // before they need to physically reach the proximity zone.
        foreach (SuspectCharacter suspect in SuspectCharacter.ActiveInstances)
        {
            UnityEngine.AI.NavMeshAgent agent = suspect.NavAgent;
            if (agent == null || !agent.enabled) continue;

            float dist = Vector3.Distance(suspect.transform.position, transform.position);
            if (dist > _suspectNavApproachRadius) continue;

            Vector3 toDestination = agent.destination - suspect.transform.position;
            if (toDestination.sqrMagnitude < 0.01f) continue;

            Vector3 toGate = transform.position - suspect.transform.position;

            // Open if the gate lies within a ~60° cone of the agent's travel direction.
            if (Vector3.Dot(toDestination.normalized, toGate.normalized) > 0.5f)
            {
                OpenForSuspect(suspect);
                return;
            }
        }
    }

    private void OpenForSuspect(SuspectCharacter suspect)
    {
        Vector3 toSuspect = suspect.transform.position - transform.position;
        bool openedIn = forwardMarker != null && Vector3.Dot(forwardMarker.forward, toSuspect) > 0f;
        ForceOpen(openedIn);
    }

    public override void OnNetworkDespawn()
    {
        _gateOpen.OnValueChanged  -= OnGateStateChanged;
        _openedIn.OnValueChanged  -= OnOpenDirectionChanged;
        _isLocked.OnValueChanged  -= OnIsLockedChanged;
    }

    // ── ILockable ─────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsLocked => _isLocked.Value;

    /// <summary>Locks the gate so it cannot be opened. Must be called on the server.</summary>
    public void Lock()
    {
        if (!IsServer) return;
        _isLocked.Value = true;
    }

    /// <summary>Unlocks the gate so players can open it. Must be called on the server.</summary>
    public void Unlock()
    {
        if (!IsServer) return;
        _isLocked.Value = false;
    }

    public override void Interact(PlayerInteractionController player)
    {
        if (_isLocked.Value)
        {
            PlayLockedTriedOpeningServerRpc();
            return;
        }

        base.Interact(player);

        if (!_beingInteractedWith)
            StartCoroutine(WaitAndToggleGate(player));
    }

    private IEnumerator WaitAndToggleGate(PlayerInteractionController player)
    {
        _beingInteractedWith = true;
        player.playerAnimationController.OpenDoor();

        // Determine open direction before the delay so local prediction is correct.
        Vector3 doorForward = forwardMarker.forward;
        Vector3 playerToDoor = transform.position - player.transform.position;
        bool openedIn = Vector3.Dot(doorForward, playerToDoor) > 0f;
        bool willBeOpen = !_gateOpen.Value;

        if (willBeOpen)
            PlayGateSoundClientRpc(true);

        // Apply visuals immediately on the interacting client — no pre-visual delay.
        ApplyGateVisuals(willBeOpen, openedIn);
        if (!willBeOpen)
            audioSource.PlayOneShot(doorCloseClip);

        ToggleGateServerRpc(openedIn, NetworkManager.Singleton.LocalClientId);

        yield return new WaitForSeconds(waitDelay);
        _beingInteractedWith = false;
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleGateServerRpc(bool openedIn, ulong senderClientId)
    {
        if (_gateOpen.Value)
        {
            _gateOpen.Value = false;
        }
        else
        {
            _openedIn.Value = openedIn;
            _gateOpen.Value = true;
        }

        // Broadcast to all clients except the one that already predicted locally.
        BroadcastGateStateClientRpc(_gateOpen.Value, _openedIn.Value, senderClientId);
    }

    /// <summary>Applies gate visuals on all clients except the one that predicted locally.</summary>
    [ClientRpc]
    private void BroadcastGateStateClientRpc(bool isOpen, bool openedIn, ulong excludeClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == excludeClientId) return;

        ApplyGateVisuals(isOpen, openedIn);
        if (!isOpen)
            audioSource.PlayOneShot(doorCloseClip);
    }

    [ClientRpc]
    private void PlayGateSoundClientRpc(bool opening)
    {
        audioSource.PlayOneShot(opening ? doorOpenClip : doorCloseClip);
    }

    /// <summary>
    /// Broadcasts the locked-tried-opening feedback (gate shake + padlock shake + sound) to all
    /// clients when a player attempts to open the gate while it is locked.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void PlayLockedTriedOpeningServerRpc() => PlayLockedTriedOpeningClientRpc();

    [ClientRpc]
    private void PlayLockedTriedOpeningClientRpc()
    {
        _animator.SetTrigger(AnimLockedShakeParam);
        _lockController?.PlayLockedAnimation();

        if (lockedSound != null)
            audioSource.PlayOneShot(lockedSound);
    }

    private void OnIsLockedChanged(bool oldValue, bool newValue) { }

    private void OnGateStateChanged(bool oldValue, bool newValue)
    {
        // Used to keep interactText consistent for late-joining clients.
    }

    private void OnOpenDirectionChanged(bool oldValue, bool newValue) { }

    private void ApplyGateVisuals(bool isOpen, bool openedIn)
    {
        _animator.SetBool("OpenedIn", isOpen && openedIn);
        _animator.SetBool("OpenedOut", isOpen && !openedIn);

        if (_navMeshObstacle != null)
            _navMeshObstacle.enabled = !isOpen;
    }

    /// <summary>Resets the gate to its closed state. Must be called on the server.</summary>
    public void Reset()
    {
        if (!IsServer) return;
        _gateOpen.Value = false;
        _openedIn.Value = false;
        ForceCloseVisualsClientRpc();
    }

    [ClientRpc]
    private void ForceCloseVisualsClientRpc()
    {
        ApplyGateVisuals(false, false);
        audioSource.PlayOneShot(doorCloseClip);
    }

    /// <summary>Opens the gate on all clients. Must be called on the server.</summary>
    public void OpenGate()
    {
        if (!IsServer) return;
        _openedIn.Value = true;
        _gateOpen.Value = true;
        BroadcastGateStateClientRpc(true, true, ulong.MaxValue);
    }

    /// <summary>
    /// Forces the gate open on all clients without requiring player interaction.
    /// Must be called on the server.
    /// </summary>
    /// <param name="openedIn">Open direction. Defaults to inward (true), matching <see cref="OpenGate"/>.</param>
    public void ForceOpen(bool openedIn = true)
    {
        if (!IsServer) return;
        _openedIn.Value = openedIn;
        _gateOpen.Value = true;
        BroadcastGateStateClientRpc(true, openedIn, ulong.MaxValue);
        PlayGateSoundClientRpc(true);
    }

    /// <summary>Closes the gate on all clients. Must be called on the server.</summary>
    public void CloseGate()
    {
        Reset();
    }

    // ── IMutantPassable ────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public bool IsBlockingMutant => !_gateOpen.Value;

    /// <inheritdoc/>
    public void OpenForMutant()
    {
        if (!IsServer) return;
        OpenGate();
        Debug.Log($"[GateController] Gate '{gameObject.name}' forced open by mutant.");
    }
}
