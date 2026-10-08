using System;
using System.Collections;
using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

public class PlayerInstance : NetworkBehaviour
{
    public static PlayerInstance Instance;

    /// <summary>
    /// Fired on the owning client the moment the local player's NetworkObject spawns.
    /// Subscribe to this to trigger any first-spawn logic (e.g. tutorial overlays).
    /// </summary>
    public static event Action OnLocalPlayerSpawned;

    /// <summary>Fired on the local player when scripted cutscene state changes.</summary>
    public event Action<bool> OnCutsceneStateChanged;

    [SerializeField] private GameObject playerLight;
    [SerializeField] private GameObject nameTag;

    [Header("Death and Spectating")]
    [SerializeField] private Unity.Cinemachine.CinemachineCamera deathCamera;
    [SerializeField] private Unity.Cinemachine.CinemachineCamera spectateCamera;
    [SerializeField] private float deathUIDelay = 2f;
    [Tooltip("Delay before the death screen re-opens after the last spectated teammate dies.")]
    [SerializeField] private float returnToDeathScreenDelay = 1f;
    [SerializeField] private AudioClip _deathStinger;

    private readonly NetworkVariable<bool> _isOutside = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsOutside => _isOutside.Value;

    // Local-only cache updated immediately on the owning client, bypassing
    // the server round-trip so UI checks see the correct state even before
    // the NetworkVariable replicates.
    private bool _isOutsideLocal;
    public bool IsOutsideLocal => _isOutsideLocal;

    /// <summary>
    /// True while this player is locked inside a scripted dialogue cutscene.
    /// Owner-write so the local client sets it; Everyone-read so the server can check it
    /// inside <see cref="MutantEnemy"/> target selection and hit-scan guards.
    /// </summary>
    private readonly NetworkVariable<bool> _isInCutscene = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    /// <summary>True while this player is in a scripted dialogue cutscene.</summary>
    public bool IsInCutscene => _isInCutscene.Value;

    /// <summary>
    /// True while this player has the end-of-shift report on screen.
    /// Owner-write (set by <see cref="UIController"/> on the local client); Everyone-read so the
    /// server can exclude the player from mutant targeting and damage.
    /// </summary>
    private readonly NetworkVariable<bool> _isViewingShiftReport = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner
    );

    /// <summary>True while this player is viewing the end-of-shift report.</summary>
    public bool IsViewingShiftReport => _isViewingShiftReport.Value;

    /// <summary>
    /// True while this player must be ignored by mutants and immune to all damage:
    /// in a dialogue cutscene or viewing the end-of-shift report.
    /// </summary>
    public bool IsProtectedFromHarm => IsInCutscene || IsViewingShiftReport;

    public bool CanControl
    {
        get => _playerMovementController.CanControl;
        set => _playerMovementController.CanControl = value;
    }

    private PlayerMovementController _playerMovementController;
    private PlayerInteractionController _playerInteractionController;
    private CharacterController _characterController;
    private PlayerCameraController _playerCameraController;
    private Unity.Netcode.Components.NetworkTransform _networkTransform;

    public PlayerInteractionController PlayerInteractionController => _playerInteractionController;
    public PlayerRadiation PlayerRadiation { get; set; }
    public PlayerHealth PlayerHealth { get; set; }
    public PlayerDrunkState PlayerDrunkState { get; set; }
    public PlayerAnimationController PlayerAnimationController { get; private set; }
    public PlayerPickupController PlayerPickupController { get; private set; }

    /// <summary>The CinemachineCamera transform managed by PlayerMovementController.</summary>
    public Transform CameraTransform => _playerMovementController?.CameraTransform;

    /// <summary>
    /// Toggles the local player's point light. Called by the dialogue system to hide the
    /// light during cutscenes so it does not bleed into scripted camera shots.
    /// </summary>
    public void SetPlayerLightActive(bool active) => playerLight?.SetActive(active);

    /// <summary>
    /// Restores the player light to its location-driven state (on outside, off inside the booth),
    /// matching <see cref="SetIsOutside"/>. Use this when ending a view/cutscene that temporarily
    /// changed the light — never force it on, or it will light up the booth (and nearby documents).
    /// </summary>
    public void RestorePlayerLightForLocation() => SetPlayerLightActive(_isOutsideLocal);

    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = GetComponent<PlayerInteractionController>();
        _characterController = GetComponent<CharacterController>();
        _playerCameraController = GetComponent<PlayerCameraController>();
        _networkTransform = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        PlayerHealth = GetComponent<PlayerHealth>();
        PlayerRadiation = GetComponent<PlayerRadiation>();
        PlayerDrunkState = GetComponent<PlayerDrunkState>();
        PlayerAnimationController = GetComponent<PlayerAnimationController>();
        PlayerPickupController = GetComponent<PlayerPickupController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (PlayerHealth != null)
        {
            PlayerHealth.OnDeath += OnAnyPlayerDeath;
            PlayerHealth.OnRespawn += OnAnyPlayerRespawn;
        }

        if (IsLocalPlayer)
        {
            // Owner-authoritative NetworkTransform: the object is instantiated by the server
            // at the correct spawn point, and that position is applied to this client's local
            // copy of the NetworkObject before this callback runs. However, the CharacterController
            // (and NetworkTransform's own interpolator, primed on first tick) can occasionally
            // "cold start" from the prefab's default local transform on a freshly-connected
            // non-host client — most visibly for the second player joining right as the host
            // starts the game. Force a clean re-sync here so the owner never renders/replicates
            // from (0,0,0) instead of the intended spawn point.
            if (_characterController != null)
                _characterController.enabled = false;

            try
            {
                // Teleport() throws if called before this NetworkObject's NetworkTransform has
                // finished its own OnNetworkSpawn (CanCommitToTransform not yet true) — sibling
                // NetworkBehaviours don't guarantee ordering, so guard against that here rather
                // than letting an exception skip CharacterController re-enable and local-player setup below.
                if (_networkTransform != null && _networkTransform.CanCommitToTransform)
                    _networkTransform.Teleport(transform.position, transform.rotation, transform.localScale);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PlayerInstance] NetworkTransform re-sync teleport skipped: {e.Message}");
            }
            finally
            {
                if (_characterController != null)
                    _characterController.enabled = true;
            }

            Instance = this;

            if (deathCamera != null) deathCamera.gameObject.SetActive(false);
            if (spectateCamera != null) spectateCamera.gameObject.SetActive(false);

            if (PlayerHealth != null)
            {
                PlayerHealth.OnDeath += Die;
                PlayerHealth.OnRespawn += Respawn;
            }

            SpectateManager.Instance?.StopSpectating();

            // A revive keeps the previous PlayerObject alive as a corpse. Its physical Camera +
            // CinemachineBrain were the local player's, so nothing else turns them off; left on,
            // the scene renders twice per frame after every revive.
            if (s_retiredLocalPlayer != null && s_retiredLocalPlayer != this)
                s_retiredLocalPlayer.ForceDisableAsRemoteCamera();
            s_retiredLocalPlayer = null;

            UIController.Instance?.HideDeathScreen();

            // A player who died earlier today and rejoined spawns already dead: skip the normal
            // gameplay restore and hand them straight to spectating.
            if (PlayerHealth != null && PlayerHealth.SpawnedDead && PlayerHealth.IsDead)
                StartCoroutine(EnterSpectateAfterDeadRejoinRoutine());
            else
                RestoreLocalGameplayStateAfterSpawn();

            OnLocalPlayerSpawned?.Invoke();

            // Continuously self-heals the "exactly one active AudioListener" invariant for the
            // lifetime of this local player object. See EnforceAudioListenerInvariantRoutine.
            if (_audioListenerEnforcementRoutine != null)
                StopCoroutine(_audioListenerEnforcementRoutine);
            _audioListenerEnforcementRoutine = StartCoroutine(EnforceAudioListenerInvariantRoutine());
        }
    }

    protected override void OnNetworkPostSpawn()
    {
        base.OnNetworkPostSpawn();

        // Every peer: a dead-rejoin player object has no body in the world (its real corpse was
        // removed when the owner left). Post-spawn so the server-side PlayerHealth has applied the
        // flag regardless of component order, and after the local branch re-enabled the CharacterController.
        if (PlayerHealth != null && PlayerHealth.SpawnedDead && PlayerHealth.IsDead)
            HideBodyForDeadRejoin();
    }

    // ── Dead rejoin ────────────────────────────────────────────────────────────

    [Tooltip("Delay after a dead-rejoin spawn before entering spectate, so the late-join bootstrap " +
             "(InitializeLateJoinClientRpc, which re-shows gameplay UI) has already run.")]
    [SerializeField] private float deadRejoinSpectateDelay = 0.5f;

    /// <summary>Components this object disabled to hide a dead-rejoin body, restored on an in-place revive.</summary>
    private readonly System.Collections.Generic.List<Behaviour> _hiddenBodyBehaviours = new System.Collections.Generic.List<Behaviour>();
    private readonly System.Collections.Generic.List<Renderer> _hiddenBodyRenderers = new System.Collections.Generic.List<Renderer>();
    private readonly System.Collections.Generic.List<Collider> _hiddenBodyColliders = new System.Collections.Generic.List<Collider>();
    private bool _isBodyHiddenForDeadRejoin;

    /// <summary>
    /// Hides every renderer and disables every collider (including the CharacterController) of a
    /// player object spawned dead for a rejoining player, plus its name tag and light.
    /// </summary>
    private void HideBodyForDeadRejoin()
    {
        if (_isBodyHiddenForDeadRejoin) return;
        _isBodyHiddenForDeadRejoin = true;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled) continue;
            r.enabled = false;
            _hiddenBodyRenderers.Add(r);
        }

        foreach (Collider c in GetComponentsInChildren<Collider>(true))
        {
            if (!c.enabled) continue;
            c.enabled = false;
            _hiddenBodyColliders.Add(c);
        }

        foreach (Light l in GetComponentsInChildren<Light>(true))
        {
            if (!l.enabled) continue;
            l.enabled = false;
            _hiddenBodyBehaviours.Add(l);
        }

        if (nameTag != null) nameTag.SetActive(false);
    }

    /// <summary>Undoes <see cref="HideBodyForDeadRejoin"/> when the player is revived in place (ResetHealth).</summary>
    private void RestoreBodyAfterDeadRejoin()
    {
        if (!_isBodyHiddenForDeadRejoin) return;
        _isBodyHiddenForDeadRejoin = false;

        foreach (Renderer r in _hiddenBodyRenderers)
            if (r != null) r.enabled = true;
        foreach (Collider c in _hiddenBodyColliders)
            if (c != null) c.enabled = true;
        foreach (Behaviour b in _hiddenBodyBehaviours)
            if (b != null) b.enabled = true;

        _hiddenBodyRenderers.Clear();
        _hiddenBodyColliders.Clear();
        _hiddenBodyBehaviours.Clear();
    }

    /// <summary>
    /// Local owner of a dead-rejoin player object: locks control like <see cref="Die"/> (without the
    /// stinger or item drop — nothing is held) and enters spectate mode. Falls back to the death
    /// screen when no living teammate is available to watch.
    /// </summary>
    private IEnumerator EnterSpectateAfterDeadRejoinRoutine()
    {
        CanControl = false;
        SetCanInteract(false);
        SetCanMove(false);

        const float readyTimeout = 5f;
        float waited = 0f;
        while ((UIController.Instance == null || SpectateManager.Instance == null) && waited < readyTimeout)
        {
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        yield return new WaitForSecondsRealtime(deadRejoinSpectateDelay);

        // Revived (or replaced) while waiting — nothing to do.
        if (Instance != this || PlayerHealth == null || !PlayerHealth.IsDead)
            yield break;

        DisableReticle();
        UIController.Instance?.ClosePlayerUI();

        if (SpectateManager.Instance != null && SpectateManager.Instance.SpectatableCount > 0)
        {
            Debug.Log("[PlayerInstance] Rejoined while dead — entering spectate mode.");
            StartSpectating();
        }
        else
        {
            Debug.Log("[PlayerInstance] Rejoined while dead with no living teammates — showing death screen.");
            ReturnToDeathScreen();
        }
    }

    private Coroutine _audioListenerEnforcementRoutine;

    /// <summary>The previous local PlayerObject, retained as a corpse by a revive, whose camera rig
    /// the replacement local player must switch off on spawn.</summary>
    private static PlayerInstance s_retiredLocalPlayer;

    /// <summary>
    /// Guarantees this client always has exactly one active AudioListener: the local player's
    /// own camera. <see cref="PlayerMovementController.OnNetworkSpawn"/> disables a remote
    /// player's Camera/AudioListener the moment that remote player's spawn message is processed
    /// locally, but on higher-latency relay transports (e.g. Steam Relay/SDR) that one-shot
    /// disable can still be pending well after this local player is already active — see
    /// <see cref="EnsureRemotePlayerCamerasDisabled"/> for the documented race. Rather than
    /// patching every call site that could race with it, this re-affirms the invariant on an
    /// interval for as long as this object is the local player, so it self-heals no matter what
    /// caused the drift (relay latency, a revive, a scripted sequence, etc.).
    /// </summary>
    private IEnumerator EnforceAudioListenerInvariantRoutine()
    {
        var wait = new WaitForSeconds(1f);
        while (this == Instance)
        {
            EnsureRemotePlayerCamerasDisabled();

            Camera ownCamera = _playerMovementController?.Camera;
            if (ownCamera != null)
            {
                if (!ownCamera.gameObject.activeSelf)
                    ownCamera.gameObject.SetActive(true);

                // While spectating, the single active listener is the spectated teammate's
                // (see SpectateManager.IsAudioListenerOnTarget), so keep our own one off.
                bool listenerHandedOff = SpectateManager.Instance != null
                    && SpectateManager.Instance.IsAudioListenerOnTarget;

                AudioListener ownListener = ownCamera.GetComponent<AudioListener>();
                if (ownListener != null && ownListener.enabled == listenerHandedOff)
                    ownListener.enabled = !listenerHandedOff;
            }

            yield return wait;
        }
    }

    /// <summary>
    /// Restores the complete local gameplay state on a fresh player object. Revives create a
    /// replacement PlayerObject, so relying on the new prefab's serialized defaults is unsafe:
    /// the death path may have independently disabled movement, look, interaction, HUD, and
    /// spectator state on the client. Active scripted dialogue remains authoritative and keeps
    /// the replacement locked until its normal exit path runs.
    ///
    /// Because the old PlayerObject's detach (which clears dialogue/cutscene flags) and this new
    /// PlayerObject's spawn happen via separate network messages, the dialogue/cutscene flags can
    /// still briefly read "active" on the exact frame this runs. A one-shot check here would then
    /// silently and permanently skip restoring control/interaction/HUD with nothing left to retry
    /// it — leaving the revived player stuck. Poll for a few frames instead of bailing out once.
    /// </summary>
    private void RestoreLocalGameplayStateAfterSpawn()
    {
        StartCoroutine(RestoreLocalGameplayStateAfterSpawnRoutine());
    }

    private IEnumerator RestoreLocalGameplayStateAfterSpawnRoutine()
    {
        const int maxAttempts = 60; // Covers detach/spawn and scene UI initialization races at low frame rates.
        int attempts = 0;

        // Only wait on cheap static flags. The reticle is not a readiness signal: Die() leaves it
        // inactive, so an active-only lookup never succeeded and stalled the HUD for every attempt.
        // EnableReticle() below finds it even while inactive.
        while (attempts < maxAttempts &&
               (ScriptedDialogueRunner.IsScriptedModeActive ||
                DialogueChoiceSystem.IsInDialogueMode ||
                UIController.Instance == null))
        {
            attempts++;
            yield return null;
        }

        // If a scripted/dialogue mode is genuinely still active after the grace period, respect it —
        // its own exit path is responsible for restoring state once it actually finishes.
        if (ScriptedDialogueRunner.IsScriptedModeActive || DialogueChoiceSystem.IsInDialogueMode)
            yield break;

        if (UIController.Instance == null)
        {
            Debug.LogWarning("[PlayerInstance] Revive recovery could not find UIController; gameplay controls were restored without HUD setup.");
        }

        if (_characterController != null)
            _characterController.enabled = true;

        if (_playerMovementController != null)
        {
            _playerMovementController.SetCanMove(true);
            _playerMovementController.SetCanLook(true);
            _playerMovementController.SetCanControl(true);
        }

        SetCanInteract(true);
        EnableReticle();
        UIController.Instance?.ShowPlayerUI();
    }


    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        DetachFromPlayerObject();
    }

    /// <summary>
    /// Releases local-player-only state when this object is retained as a corpse while a
    /// replacement player object is spawned for the same client.
    /// </summary>
    public void DetachFromPlayerObject()
    {
        if (PlayerHealth != null)
        {
            PlayerHealth.OnDeath -= OnAnyPlayerDeath;
            PlayerHealth.OnRespawn -= OnAnyPlayerRespawn;
        }

        if (deathCamera != null) deathCamera.gameObject.SetActive(false);
        if (spectateCamera != null) spectateCamera.gameObject.SetActive(false);

        if (Instance != this)
        {
            // The replacement already spawned locally before this detach arrived; shut this
            // rig off now since the replacement's spawn-time cleanup could not see it.
            // _audioListenerEnforcementRoutine is only ever started on the local player.
            if (Instance != null && _audioListenerEnforcementRoutine != null)
            {
                ForceDisableAsRemoteCamera();
                StopCoroutine(_audioListenerEnforcementRoutine);
                _audioListenerEnforcementRoutine = null;
            }
            return;
        }

        // The replacement's OnNetworkSpawn disables this rig, so the screen never goes black
        // between this detach and the new player's spawn.
        s_retiredLocalPlayer = this;

        // This object is about to become a corpse; the replacement player object's camera owns
        // the listener from its spawn onwards. If spectating, the teammate's listener stays live
        // until the replacement spawns and SpectateManager.StopSpectating hands it back.
        SetOwnAudioListenerEnabled(false);

        DialogueChoiceSystem.Instance?.AbortForPlayerObjectReplacement();

        if (PlayerHealth != null)
        {
            PlayerHealth.OnDeath -= Die;
            PlayerHealth.OnRespawn -= Respawn;
        }

        if (_audioListenerEnforcementRoutine != null)
        {
            StopCoroutine(_audioListenerEnforcementRoutine);
            _audioListenerEnforcementRoutine = null;
        }

        Instance = null;
    }

    private void OnAnyPlayerDeath()
    {
        if (nameTag != null) nameTag.SetActive(false);
    }

    private void OnAnyPlayerRespawn()
    {
        RestoreBodyAfterDeadRejoin();
        if (nameTag != null) nameTag.SetActive(true);
    }

    public void SetIsOutside(bool value)
    {
        _isOutside.Value = value;
        _isOutsideLocal = value;
        // A hidden dead-rejoin body must not light up its spawn point.
        playerLight.SetActive(value && !_isBodyHiddenForDeadRejoin);
    }

    /// <summary>
    /// Sets the cutscene state for this player's <see cref="_isInCutscene"/> NetworkVariable.
    /// Must only be called on the owning client (i.e. from <see cref="DialogueChoiceSystem"/>
    /// on <see cref="Instance"/>). The server reads this to prevent mutants from aggroing
    /// or damaging players who are currently locked inside a scripted dialogue.
    /// </summary>
    public void SetIsInCutscene(bool value)
    {
        _isInCutscene.Value = value;
        OnCutsceneStateChanged?.Invoke(value);
    }

    /// <summary>
    /// Sets <see cref="IsViewingShiftReport"/>. Must only be called on the owning client
    /// (i.e. on <see cref="Instance"/> from <see cref="UIController"/>).
    /// </summary>
    public void SetIsViewingShiftReport(bool value)
    {
        if (!IsOwner || _isViewingShiftReport.Value == value)
            return;
        _isViewingShiftReport.Value = value;
    }

    /// <summary>
    /// Sets the player's outside state from any context.
    /// Updates <see cref="IsOutsideLocal"/> immediately on the calling client so
    /// that UI checks don't have to wait for the NetworkVariable server round-trip.
    /// Routes the authoritative write through a ServerRpc when called on a client.
    /// </summary>
    public void RequestSetIsOutside(bool value)
    {
        _isOutsideLocal = value;

        if (IsServer)
            SetIsOutside(value);
        else
            SetIsOutsideServerRpc(value);
    }

    [ServerRpc(RequireOwnership = false)]
    private void SetIsOutsideServerRpc(bool value)
    {
        SetIsOutside(value);
    }

    public void OpenedUIPanel()
    {
        SetCanInteract(false);
        _playerMovementController.SetCanControl(false);
    }

    public void ClosedUIPanel()
    {
        SetCanInteract(true);
        _playerMovementController.SetCanControl(true);
    }

    public void SetCanInteract(bool value, string interactText = "")
    {
        _playerInteractionController.SetCanInteract(value, interactText);
    }

    public void SetCanMove(bool value)
    {
        _playerMovementController.SetCanMove(value);
    }

    /// <summary>
    /// Resets the camera pitch and local rotation to a neutral forward-looking orientation.
    /// Use this after teleporting the player so the camera doesn't retain a stale look angle.
    /// </summary>
    public void ResetCameraOrientation()
    {
        _playerMovementController?.ResetCameraRotation();
    }

    /// <summary>
    /// Enables or disables this player's own first-person <see cref="Unity.Cinemachine.CinemachineCamera"/>
    /// vcam GameObject. Use this during scripted sequences (e.g. the intro cutscene) so
    /// CinemachineBrain drops it from consideration and blends onto the cutscene's own vcam
    /// instead — e.g. the intro cutscene's Giorgi-character head-mounted CinemachineCamera
    /// (Priority 25), which lives under the cutscene's PlayableDirector hierarchy and becomes
    /// active once the cutscene GameObject activates.
    /// Deliberately does NOT touch the physical <see cref="UnityEngine.Camera"/>/CinemachineBrain
    /// GameObject — that must stay enabled the whole time, since it's what renders whichever
    /// vcam (the player's own, or the cutscene's) is currently highest priority. Disabling it
    /// disables the Brain itself, leaving nothing to render — a black screen.
    /// </summary>
    public void SetOwnCameraActive(bool active)
    {
        Transform cameraTransform = _playerMovementController?.CameraTransform;
        if (cameraTransform != null)
            cameraTransform.gameObject.SetActive(active);
    }

    /// <summary>
    /// Defensive resync, call before a scripted sequence hands the screen to a scene vcam
    /// (e.g. the intro cutscene). For every OTHER connected player's <see cref="PlayerInstance"/>,
    /// forces their camera rig into the same disabled state <see cref="PlayerMovementController.OnNetworkSpawn"/>
    /// normally applies for remote players on spawn.
    /// <see cref="PlayerMovementController.OnNetworkSpawn"/> disables a remote player's
    /// MainCamera-tagged Camera (and vcam) the moment that remote player's NetworkObject spawn
    /// message is processed locally. Over low-latency transports (LAN/Unity Transport) this has
    /// always resolved well before any scripted sequence starts. Over higher-latency relay
    /// transports (e.g. Steam Relay/SDR) — especially for a client that only just joined — that
    /// spawn callback can still be pending at the exact moment the intro cutscene starts, leaving
    /// two MainCamera-tagged Camera components (and two AudioListeners) simultaneously enabled on
    /// that client. That's what produces an erratic/flickering camera specifically for the
    /// non-host client under relay, while LAN's near-instant spawn sync hides the race entirely.
    /// Safe to call redundantly — a no-op for any player whose remote camera is already disabled.
    /// </summary>
    public static void EnsureRemotePlayerCamerasDisabled()
    {
        var networkManager = Unity.Netcode.NetworkManager.Singleton;
        if (networkManager == null) return;

        foreach (var client in networkManager.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            var playerInstance = client.PlayerObject.GetComponent<PlayerInstance>();
            if (playerInstance == null || playerInstance.IsLocalPlayer) continue;

            playerInstance.ForceDisableAsRemoteCamera();
        }
    }

    /// <summary>
    /// Forces this player's camera rig (vcam + physical Camera/AudioListener) into the disabled
    /// state <see cref="PlayerMovementController.OnNetworkSpawn"/> applies for remote players.
    /// See <see cref="EnsureRemotePlayerCamerasDisabled"/> for why this needs to be re-affirmable
    /// on demand rather than relying solely on the one-time OnNetworkSpawn callback.
    /// </summary>
    private void ForceDisableAsRemoteCamera()
    {
        // The vcam of the teammate currently being spectated is intentionally active (enabled by
        // SpectateManager via SetSpectatedByCamera). The local player's audio-listener enforcement
        // routine keeps running while dead, so without this guard it would switch the spectated
        // vcam off within a second, leaving the brain with no live vcam and a frozen view.
        bool isSpectatedTarget = SpectateManager.Instance != null
            && SpectateManager.Instance.IsSpectating
            && SpectateManager.Instance.CurrentTarget == this;

        Transform cameraTransform = _playerMovementController?.CameraTransform;
        if (cameraTransform != null && !isSpectatedTarget)
            cameraTransform.gameObject.SetActive(false);

        // The spectated teammate's Camera rig runs in listener-only mode (Camera and
        // CinemachineBrain disabled, AudioListener enabled) and must be left alone.
        if (_spectatorListenerActive)
            return;

        Camera camera = _playerMovementController?.Camera;
        if (camera != null)
        {
            camera.gameObject.SetActive(false);

            // Belt-and-suspenders: explicitly disable the AudioListener too, rather than relying
            // solely on the GameObject's active state cascading to it.
            AudioListener listener = camera.GetComponent<AudioListener>();
            if (listener != null)
                listener.enabled = false;
        }
    }

    /// <summary>
    /// Kills the local player: disables movement and interaction, then activates the death camera.
    /// Ragdoll activation is handled automatically by <see cref="RagdollController"/> via OnDeath.
    /// </summary>
    public void Die()
    {
        if (_deathStinger != null)
            SFXController.Instance?.Play(_deathStinger);

        CanControl = false;
        SetCanInteract(false);
        SetCanMove(false);
        DisableReticle();

        DropHeldItemOnDeath();

        if (deathCamera != null)
        {
            deathCamera.gameObject.SetActive(true);
            deathCamera.Priority = 100;
        }
        else
        {
            Debug.LogWarning("[PlayerInstance] Death Camera reference is null!");
        }

        // Notify UI to show death screen after delay and hide the HUD
        if (IsLocalPlayer)
        {
            UIController.Instance?.ClosePlayerUI();
            UIController.Instance?.ShowDeathScreen(deathUIDelay);
        }
    }

    /// <summary>
    /// Owner-side half of the death drop. The server drops the held item and every stowed
    /// inventory item with real physics and no throw force (see
    /// <see cref="PlayerInventory.DropAllItemsOnDeathServer"/>); here the dead client only
    /// forgets them locally — hand IK, equip containers, the hand constraint, and hotbar slots —
    /// so nothing keeps the items pinned to the corpse on this machine.
    /// </summary>
    private void DropHeldItemOnDeath()
    {
        PlayerInventory inventory = GetComponent<PlayerInventory>();
        if (inventory != null)
        {
            inventory.ClearAllSlotsLocalOnDeath();
            return;
        }

        PlayerPickupController?.ReleaseHeldObjectLocallyForServerDrop();
    }

    /// <summary>
    /// Resurrects the local player: restores control, re-enables cameras, and stops spectating.
    /// Visuals (head, arms) are restored via SetSpectatorMode(false).
    /// </summary>
    public void Respawn()
    {
        CanControl = true;
        SetCanInteract(true);
        SetCanMove(true);
        EnableReticle();

        if (deathCamera != null)
        {
            deathCamera.Priority = 0;
            deathCamera.gameObject.SetActive(false);
        }

        // Re-enable own first-person camera
        _playerMovementController.CameraTransform.gameObject.SetActive(true);

        // Ensure we stop spectating if we were in spectate mode
        SpectateManager.Instance?.StopSpectating();

        // Restore local visuals (head scale and arms)
        PlayerAnimationController?.SetSpectatorMode(false);

        UIController.Instance?.ShowPlayerUI();

        Debug.Log("[PlayerInstance] Player respawned.");
    }

    public void StartSpectating()
    {
        if (deathCamera != null)
        {
            deathCamera.Priority = 0;
            deathCamera.gameObject.SetActive(false);
        }

        // Deactivate the dead player's own first-person camera so only the
        // spectated player's CinemachineCamera is active for this client's brain.
        _playerMovementController.CameraTransform.gameObject.SetActive(false);

        // Clear the blood-splatter / hurt overlay so it doesn't persist during spectating.
        UIController.Instance?.ScreenDamage?.Hide();
        var damageVignette = FindFirstObjectByType<GoodCopBadCop.Effects.DamageVignetteView>();
        if (damageVignette != null)
            damageVignette.Hide();
        var glitchEffect = FindFirstObjectByType<GoodCopBadCop.Effects.GlitchEffectView>();
        if (glitchEffect != null)
            glitchEffect.Hide();

        SpectateManager.Instance?.StartSpectating();
        Debug.Log("[PlayerInstance] Started spectating.");
    }

    /// <summary>
    /// Reverses <see cref="StartSpectating"/> for a still-dead local player: stops spectating,
    /// puts the death camera back on this player's body, and re-opens the death screen.
    /// Called by SpectateManager when the last living teammate dies or leaves.
    /// </summary>
    public void ReturnToDeathScreen()
    {
        if (!IsLocalPlayer) return;

        SpectateManager.Instance?.StopSpectating();

        if (deathCamera != null)
        {
            deathCamera.gameObject.SetActive(true);
            deathCamera.Priority = 100;
        }

        UIController.Instance?.ScreenDamage?.Hide();
        UIController.Instance?.ShowDeathScreen(returnToDeathScreenDelay);
        Debug.Log("[PlayerInstance] Returned to death screen from spectating.");
    }

    /// <summary>
    /// Activates or deactivates this player's CinemachineCamera for a spectating client.
    /// When active, the camera is given priority 100 so the spectating client's
    /// CinemachineBrain picks it up as the live camera.
    /// Also switches the held-item follow target between body arm (normal) and camera arm
    /// (spectating) to eliminate NetworkAnimator lag on held objects.
    /// </summary>
    public void SetSpectatedByCamera(bool spectated)
    {
        Transform camTransform = _playerMovementController?.CameraTransform;
        if (camTransform == null) return;

        var cinemachineCam = camTransform.GetComponent<Unity.Cinemachine.CinemachineCamera>();
        if (spectated)
        {
            camTransform.gameObject.SetActive(true);
            if (cinemachineCam != null) cinemachineCam.Priority = 100;
        }
        else
        {
            if (cinemachineCam != null) cinemachineCam.Priority = 0;
            camTransform.gameObject.SetActive(false);
        }

        GetComponent<PlayerPickupController>()?.SetSpectatedView(spectated);
    }

    private bool _spectatorListenerActive;
    private Vector3 _spectatorListenerRestoreLocalPos;
    private Quaternion _spectatorListenerRestoreLocalRot;

    /// <summary>True while this remote player's AudioListener is the live one for a spectating client.</summary>
    public bool IsSpectatorListenerActive => _spectatorListenerActive;

    /// <summary>
    /// Switches this remote player's physical Camera rig into listener-only mode for a dead
    /// local player who is spectating them. The GameObject is on, the <see cref="Camera"/> and
    /// <see cref="Unity.Cinemachine.CinemachineBrain"/> are off so nothing extra renders, and the
    /// <see cref="AudioListener"/> is on and follows this player's vcam pose every LateUpdate.
    /// Passing false restores the default remote state (rig inactive, listener off).
    /// </summary>
    public void SetSpectatorAudioListener(bool active)
    {
        if (IsLocalPlayer) return;

        Camera cam = _playerMovementController?.Camera;
        if (cam == null)
        {
            _spectatorListenerActive = false;
            return;
        }

        var brain = cam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
        var listener = cam.GetComponent<AudioListener>();

        if (active)
        {
            if (listener == null || _spectatorListenerActive) return;

            // Disable renderers before activating the GameObject so their OnEnable never runs.
            cam.enabled = false;
            if (brain != null) brain.enabled = false;
            listener.enabled = true;

            _spectatorListenerRestoreLocalPos = cam.transform.localPosition;
            _spectatorListenerRestoreLocalRot = cam.transform.localRotation;
            cam.gameObject.SetActive(true);

            _spectatorListenerActive = true;
            SyncSpectatorListenerPose();
        }
        else
        {
            if (!_spectatorListenerActive) return;
            _spectatorListenerActive = false;

            if (listener != null) listener.enabled = false;
            cam.gameObject.SetActive(false);
            cam.transform.localPosition = _spectatorListenerRestoreLocalPos;
            cam.transform.localRotation = _spectatorListenerRestoreLocalRot;
            cam.enabled = true;
            if (brain != null) brain.enabled = true;
        }
    }

    /// <summary>Enables or disables the AudioListener on this player's own physical Camera.</summary>
    public void SetOwnAudioListenerEnabled(bool enabled)
    {
        Camera ownCamera = _playerMovementController?.Camera;
        if (ownCamera == null) return;

        AudioListener listener = ownCamera.GetComponent<AudioListener>();
        if (listener != null && listener.enabled != enabled)
            listener.enabled = enabled;
    }

    private void LateUpdate()
    {
        if (_spectatorListenerActive)
            SyncSpectatorListenerPose();
    }

    private void SyncSpectatorListenerPose()
    {
        Transform camTransform = _playerMovementController?.CameraTransform;
        Camera cam = _playerMovementController?.Camera;
        if (camTransform == null || cam == null) return;

        cam.transform.SetPositionAndRotation(camTransform.position, camTransform.rotation);
    }

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsLocalPlayer && !GoodCopBadCop.Input.TextInputFocus.IsCapturingKeyboard && Input.GetKeyDown(KeyCode.K))
            PlayerHealth?.TakeDamage(PlayerHealth.MaxHealth, EffectKeys.PlayerDeath);
#endif
    }

    /// <summary>
    /// Deactivates almost all components on this player instance, effectively turning it into
    /// a static "corpse" or background object. Used when the player is replaced by a fresh
    /// respawned instance.
    /// </summary>
    public void DeactivateAllComponents()
    {
        // 1. Disable local-only components
        if (_playerMovementController != null) _playerMovementController.enabled = false;
        if (_playerInteractionController != null) _playerInteractionController.enabled = false;
        if (_characterController != null) _characterController.enabled = false;
        if (_playerCameraController != null) _playerCameraController.enabled = false;
        if (PlayerHealth != null) PlayerHealth.enabled = false;
        if (PlayerRadiation != null) PlayerRadiation.enabled = false;
        if (PlayerDrunkState != null) PlayerDrunkState.enabled = false;

        // 2. Disable Networking
        var nt = GetComponent<Unity.Netcode.Components.NetworkTransform>();
        if (nt != null) nt.enabled = false;
        var na = GetComponent<Unity.Netcode.Components.NetworkAnimator>();
        if (na != null) na.enabled = false;

        // 3. Keep visual/animation hierarchy but stop the animator itself if needed
        var animator = GetComponent<Animator>();
        if (animator != null) animator.enabled = false;

        // 4. Disable name tag and light
        if (nameTag != null) nameTag.SetActive(false);
        if (playerLight != null) playerLight.SetActive(false);

        Debug.Log($"[PlayerInstance] Components deactivated for corpse of player {OwnerClientId}.");
    }

    /// <summary>
    /// Moves this player to <paramref name="position"/>. This player's <c>NetworkTransform</c>
    /// is configured with <c>AuthorityMode.Owner</c> (not server-authoritative) — only the
    /// owning client's writes are committed and broadcast to observers. A write from anyone
    /// else (including the server, when the server isn't also the owner) is silently ignored
    /// by the NetworkTransform and gets overwritten by the next interpolation tick, which is
    /// why teleports like the post-intro-cutscene booth placement previously worked for the
    /// host (owner == server) but silently no-op'd for non-host clients (owner != server).
    /// Route the write to whichever client actually owns this object.
    /// </summary>
    public void SetPosition(Transform position)
    {
        if (IsOwner)
        {
            transform.position = position.position;
            transform.rotation = position.rotation;
        }
        else
        {
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            SetPositionClientRpc(position.position, position.rotation, rpcParams);
        }
    }

    [ClientRpc]
    private void SetPositionClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
    {
        transform.position = position;
        transform.rotation = rotation;
    }

    public void DisableReticle()
    {
        // Same lookup as EnableReticle: a freshly spawned player (e.g. a dead rejoin) has no
        // cached reference yet.
        if (_playerInteractionController.reticle == null)
            _playerInteractionController.reticle =
                FindFirstObjectByType<ReticleController>(FindObjectsInactive.Include);

        if (_playerInteractionController.reticle != null)
            _playerInteractionController.reticle.gameObject.SetActive(false);
    }

    public void EnableReticle()
    {
        // The reticle is a scene object, not a prefab child. On a fresh spawn after revival
        // the controller's cached reference is null, and the reticle may still be inactive
        // from the previous death, so FindFirstObjectByType won't find it unless we explicitly
        // include inactive objects.
        if (_playerInteractionController.reticle == null)
            _playerInteractionController.reticle =
                FindFirstObjectByType<ReticleController>(FindObjectsInactive.Include);

        if (_playerInteractionController.reticle != null)
            _playerInteractionController.reticle.gameObject.SetActive(true);
    }

    public Camera GetCamera()
    {
        return _playerMovementController.Camera;
    }

    public void Heal(float healAmount)
    {
        Heal(healAmount, EffectKeys.PlayerHeal);
    }

    public void Heal(float healAmount, string effectKey)
    {
        PlayerHealth.Heal(healAmount, effectKey);
    }
}
