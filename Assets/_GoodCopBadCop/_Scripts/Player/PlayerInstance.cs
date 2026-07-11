using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

public class PlayerInstance : NetworkBehaviour
{
    public static PlayerInstance Instance;

    [SerializeField] private GameObject playerLight;
    [SerializeField] private GameObject nameTag;

    [Header("Death and Spectating")]
    [SerializeField] private Unity.Cinemachine.CinemachineCamera deathCamera;
    [SerializeField] private Unity.Cinemachine.CinemachineCamera spectateCamera;
    [SerializeField] private float deathUIDelay = 2f;

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
            _playerMovementController.CanControl = true;
            Instance = this;

            if (deathCamera != null) deathCamera.gameObject.SetActive(false);
            if (spectateCamera != null) spectateCamera.gameObject.SetActive(false);

            PlayerHealth.OnDeath += Die;
            PlayerHealth.OnRespawn += Respawn;

            // Clean up any lingering death/spectator state from a previous player object
            // (e.g. when this is a fresh spawn after the player was revived).
            SpectateManager.Instance?.StopSpectating();
            UIController.Instance?.HideDeathScreen();

            // Revival via despawn+respawn creates a fresh NetworkObject and never fires
            // PlayerHealth.OnRespawn, so Respawn() is never invoked. Re-enable the reticle
            // and reset interaction state explicitly so the player can interact immediately.
            // On a first-ever spawn these calls are harmless no-ops.
            EnableReticle();
            SetCanInteract(true);
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
    /// Kills the local player: disables movement and interaction, then activates the death camera.
    /// Ragdoll activation is handled automatically by <see cref="RagdollController"/> via OnDeath.
    /// </summary>
    public void Die()
    {
        CanControl = false;
        SetCanInteract(false);
        SetCanMove(false);
        DisableReticle();

        if (deathCamera != null)
        {
            deathCamera.gameObject.SetActive(true);
            deathCamera.Priority = 100;
        }
        else
        {
            Debug.LogWarning("[PlayerInstance] Death Camera reference is null!");
        }

        // Notify UI to show death screen after delay
        if (IsLocalPlayer)
        {
            UIController.Instance?.ShowDeathScreen(deathUIDelay);
        }
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

    public void SetPosition(Transform position)
    {
        transform.position = position.position;
        transform.rotation = position.rotation;
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
