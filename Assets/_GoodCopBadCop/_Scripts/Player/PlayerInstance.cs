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

    /// <summary>
    /// Always-active transform that mirrors this player's camera world position and rotation.
    /// Used by spectators as a precise follow target that reflects DOTween and sequence moves.
    /// </summary>
    public Transform CameraTransform => _playerMovementController?.SpectateTarget;

    private bool _isSpectating;
    private Transform _spectateFollowTarget;

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

        if (IsLocalPlayer)
        {
            _playerMovementController.CanControl = true;
            Instance = this;

            if (deathCamera != null) deathCamera.gameObject.SetActive(false);
            if (spectateCamera != null) spectateCamera.gameObject.SetActive(false);

            if (PlayerHealth != null)
            {
                PlayerHealth.OnDeath += Die;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (IsLocalPlayer && PlayerHealth != null)
        {
            PlayerHealth.OnDeath -= Die;
        }
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

    public void StartSpectating()
    {
        if (spectateCamera != null)
        {
            if (deathCamera != null)
            {
                deathCamera.Priority = 0;
                deathCamera.gameObject.SetActive(false);
            }

            // Null out Cinemachine follow/aim — the spectateCamera has no body or aim
            // components, so we drive its transform directly each Update for exact matching.
            spectateCamera.Follow = null;
            spectateCamera.LookAt = null;
            spectateCamera.gameObject.SetActive(true);
            spectateCamera.Priority = 100;

            _isSpectating = true;
            SpectateManager.Instance?.StartSpectating();
            Debug.Log($"[PlayerInstance] Spectate Camera Activated. Priority set to {spectateCamera.Priority}.");
        }
        else
        {
            Debug.LogWarning("[PlayerInstance] Spectate Camera reference is null!");
        }
    }

    /// <summary>
    /// Sets the world-space transform that the spectate camera will track each frame.
    /// The target should be the spectated player's <see cref="PlayerMovementController.SpectateTarget"/>.
    /// </summary>
    public void SetSpectateTarget(Transform target)
    {
        _spectateFollowTarget = target;
    }

    private void Update()
    {
        // Drive the spectate camera's transform directly each frame so it precisely matches
        // the spectated player's camera (including DOTween sequences and cinematic moves).
        if (_isSpectating && _spectateFollowTarget != null && spectateCamera != null)
        {
            spectateCamera.transform.SetPositionAndRotation(
                _spectateFollowTarget.position,
                _spectateFollowTarget.rotation);
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Debug: K instantly kills the local player for testing death/spectate flow.
        if (IsLocalPlayer && Input.GetKeyDown(KeyCode.K))
        {
            PlayerHealth?.TakeDamage(PlayerHealth.MaxHealth);
        }
#endif
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
