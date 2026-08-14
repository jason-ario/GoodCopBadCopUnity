using System;
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

            _playerMovementController.CanControl = true;
            Instance = this;

            if (deathCamera != null) deathCamera.gameObject.SetActive(false);
            if (spectateCamera != null) spectateCamera.gameObject.SetActive(false);

            PlayerHealth.OnDeath += Die;
            PlayerHealth.OnRespawn += Respawn;

            SpectateManager.Instance?.StopSpectating();
            UIController.Instance?.HideDeathScreen();

            EnableReticle();
            SetCanInteract(true);

            OnLocalPlayerSpawned?.Invoke();
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (PlayerHealth != null)
        {
            PlayerHealth.OnDeath -= OnAnyPlayerDeath;
            PlayerHealth.OnRespawn -= OnAnyPlayerRespawn;
        }

        if (IsLocalPlayer)
        {
            if (PlayerHealth != null)
            {
                PlayerHealth.OnDeath -= Die;
                PlayerHealth.OnRespawn -= Respawn;
            }

            if (Instance == this)
            {
                Instance = null;
            }
        }
    }

    private void OnAnyPlayerDeath()
    {
        if (nameTag != null) nameTag.SetActive(false);
    }

    private void OnAnyPlayerRespawn()
    {
        if (nameTag != null) nameTag.SetActive(true);
    }

    public void SetIsOutside(bool value)
    {
        _isOutside.Value = value;
        _isOutsideLocal = value;
        playerLight.SetActive(value);
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
        Transform cameraTransform = _playerMovementController?.CameraTransform;
        if (cameraTransform != null)
            cameraTransform.gameObject.SetActive(false);

        Camera camera = _playerMovementController?.Camera;
        if (camera != null)
            camera.gameObject.SetActive(false);
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
    /// Releases whatever item the player is holding when they die, unhooking it from the
    /// character (parent constraint, holder/ownership state, equip containers — same as a
    /// normal throw release) and giving it a small physics impulse so it tumbles to the
    /// ground with its collider/rigidbody active, instead of floating in place attached to
    /// the corpse. Leaves it fully interactable so other players can pick it up.
    /// </summary>
    private void DropHeldItemOnDeath()
    {
        if (PlayerPickupController == null || !PlayerPickupController.IsHoldingObject)
            return;

        PickableObject droppedItem = PlayerPickupController.ReleaseHeldObjectForThrow();
        if (droppedItem == null)
            return;

        Vector3 velocity = (Vector3.up * 0.5f) + (transform.forward * 0.5f);
        droppedItem.ThrowServerRpc(droppedItem.transform.position, velocity);
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

        SpectateManager.Instance?.StartSpectating();
        Debug.Log("[PlayerInstance] Started spectating.");
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

    private void Update()
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        if (IsLocalPlayer && Input.GetKeyDown(KeyCode.K))
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
