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
    public PlayerAnimationController PlayerAnimationController { get; private set; }

    /// <summary>The CinemachineCamera transform managed by PlayerMovementController.</summary>
    public Transform CameraTransform => _playerMovementController?.CameraTransform;

    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = GetComponent<PlayerInteractionController>();
        _characterController = GetComponent<CharacterController>();
        _playerCameraController = GetComponent<PlayerCameraController>();
        PlayerHealth = GetComponent<PlayerHealth>();
        PlayerRadiation = GetComponent<PlayerRadiation>();
        PlayerAnimationController = GetComponent<PlayerAnimationController>();
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

        if (IsLocalPlayer && PlayerHealth != null)
        {
            PlayerHealth.OnDeath -= Die;
            PlayerHealth.OnRespawn -= Respawn;
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
            PlayerHealth?.TakeDamage(PlayerHealth.MaxHealth);
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

    [ServerRpc]
    public void RequestRespawnServerRpc(ulong targetClientId, NetworkObjectReference corpseRef)
    {
        if (!IsServer) return;

        // 1. Deactivate components on the old player instance (the "corpse")
        if (corpseRef.TryGet(out NetworkObject corpseObj))
        {
            var corpseInstance = corpseObj.GetComponent<PlayerInstance>();
            if (corpseInstance != null)
            {
                corpseObj.RemoveOwnership(); 
                corpseInstance.DeactivateAllComponents();
                corpseObj.name = "Player_Corpse_" + targetClientId;
            }
        }

        // 2. Spawn a fresh player instance for that client
        if (PlayerSpawner.Instance != null)
        {
            bool isSinglePlayer = NetworkManager.Singleton.ConnectedClients.Count <= 1;
            PlayerSpawner.Instance.SpawnPlayer(targetClientId, isSinglePlayer);
        }
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
        _playerInteractionController.reticle.gameObject.SetActive(true);
    }

    public Camera GetCamera()
    {
        return _playerMovementController.Camera;
    }

    public void Heal(float healAmount)
    {
        PlayerHealth.Heal(healAmount);
    }
}
