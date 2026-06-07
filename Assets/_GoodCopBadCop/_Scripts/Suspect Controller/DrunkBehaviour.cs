using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Attach to a suspect prefab root to give that character a chance of spawning drunk each shift.
/// When active, swaps the animator to the drunk override controller and provides drunk-specific
/// entry dialogue that overrides the normal SuspectData lines.
/// Initialization is triggered by SuspectCharacter at the end of its Initialize* methods.
/// </summary>
public class DrunkBehaviour : NetworkBehaviour
{
    [Header("Drunk Chance")]
    [Tooltip("Probability (0–1) that this suspect spawns drunk each shift.")]
    [SerializeField] [Range(0f, 1f)] private float _drunkChance = 0.2f;

    [Header("References")]
    [Tooltip("Animator on the character mesh child (the same target as SuspectCharacter.animator).")]
    [SerializeField] private Animator _animator;
    [Tooltip("The Drunk.overrideController asset to apply when the suspect is drunk.")]
    [SerializeField] private AnimatorOverrideController _drunkAnimatorController;

    [Header("Drunk Dialogue")]
    [SerializeField] private DrunkDialogueSet _drunkDialogues;

    private readonly NetworkVariable<bool> _isDrunk = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>True when this suspect has been initialised as drunk for the current shift.</summary>
    public bool IsDrunk => _isDrunk.Value;

    /// <summary>
    /// Rolls the drunk chance (or honours ForceNextSuspectDrunk) and applies the drunk state
    /// if the roll succeeds. Must be called from the server — the NetworkVariable replicates
    /// automatically and the animator swap is pushed to clients via ClientRpc.
    /// Called by SuspectCharacter at the end of every Initialize* method.
    /// </summary>
    public void TryActivate()
    {
        if (!IsServer) return;

        bool forceDrunk = SuspectController.ForceNextSuspectDrunk;
        if (forceDrunk)
        {
            SuspectController.ForceNextSuspectDrunk = false;
        }
        else if (!(Random.value < _drunkChance))
        {
            return;
        }

        if (_animator == null)
        {
            Debug.LogWarning("[DrunkBehaviour] _animator is not assigned — cannot apply drunk animator.");
            return;
        }

        if (_drunkAnimatorController == null)
        {
            Debug.LogWarning("[DrunkBehaviour] _drunkAnimatorController is not assigned — cannot apply drunk animator.");
            return;
        }

        _isDrunk.Value = true;
        ApplyAnimatorClientRpc();
        Debug.Log($"[DrunkBehaviour] {gameObject.name} is drunk this shift.");
    }

    /// <summary>
    /// Swaps the suspect's animator controller to the drunk override on all clients.
    /// </summary>
    [ClientRpc]
    private void ApplyAnimatorClientRpc()
    {
        if (IsServer) return;
        ApplyDrunkAnimator();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        // Clients that join late catch up via the NetworkVariable change callback.
        _isDrunk.OnValueChanged += OnIsDrunkChanged;

        // If already drunk when this client spawns (e.g. late join), apply immediately.
        if (_isDrunk.Value && !IsServer)
            ApplyDrunkAnimator();
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isDrunk.OnValueChanged -= OnIsDrunkChanged;
    }

    private void OnIsDrunkChanged(bool previous, bool current)
    {
        if (current && !IsServer)
            ApplyDrunkAnimator();
    }

    private void ApplyDrunkAnimator()
    {
        if (_animator == null || _drunkAnimatorController == null) return;
        _animator.runtimeAnimatorController = _drunkAnimatorController;
    }

    /// <summary>
    /// Returns a drunk-specific entry dialogue line for the current day band.
    /// Returns null if the relevant dialogue array is empty or null (so the caller can fall back).
    /// </summary>
    public string GetDrunkEntryDialogue()
    {
        string[] pool;

        if (ShiftManager.Instance.IsEarlyDays)
            pool = _drunkDialogues.earlyDays;
        else if (ShiftManager.Instance.IsMidDays)
            pool = _drunkDialogues.midDays;
        else
            pool = _drunkDialogues.finalDays;

        if (pool == null || pool.Length == 0)
        {
            Debug.LogWarning($"[DrunkBehaviour] No drunk dialogue authored for the current day band on '{gameObject.name}'.");
            return null;
        }

        return pool[Random.Range(0, pool.Length)];
    }

    // ──────────────────────────────────────────────────────────────────────────────
    // Nested type
    // ──────────────────────────────────────────────────────────────────────────────

    [System.Serializable]
    public struct DrunkDialogueSet
    {
        [Tooltip("Drunk entry lines for days 1–10.")]
        [TextArea(1, 3)] public string[] earlyDays;
        [Tooltip("Drunk entry lines for days 11–20.")]
        [TextArea(1, 3)] public string[] midDays;
        [Tooltip("Drunk entry lines for days 21–30.")]
        [TextArea(1, 3)] public string[] finalDays;
    }
}
