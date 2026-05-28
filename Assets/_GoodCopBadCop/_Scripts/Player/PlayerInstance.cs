using Unity.Netcode;
using UnityEngine;

public class PlayerInstance : NetworkBehaviour
{
    public static PlayerInstance Instance;

    /// <summary>World-space position used as a holding area until the player is activated.</summary>
    private static readonly Vector3 DormantPosition = new Vector3(0f, -500f, 0f);

    [SerializeField] private GameObject playerLight;
    [SerializeField] private GameObject nameTag;
    [SerializeField] private RagdollController ragdollController;

    private readonly NetworkVariable<bool> _isOutside = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    /// <summary>
    /// Tracks whether the player has been activated (teleported to a spawn point and controls enabled).
    /// False immediately after auto-spawn; set to true by the server via ActivateAtPoint.
    /// </summary>
    private readonly NetworkVariable<bool> _isActivated = new NetworkVariable<bool>(
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

    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = GetComponent<PlayerInteractionController>();
        _characterController = GetComponent<CharacterController>();
        _playerCameraController = GetComponent<PlayerCameraController>();
        PlayerHealth = GetComponent<PlayerHealth>();
        PlayerRadiation = GetComponent<PlayerRadiation>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isActivated.OnValueChanged += OnActivatedChanged;

        // Server moves the player to a safe holding area and disables
        // the CharacterController so gravity does not run while dormant.
        if (IsServer)
        {
            _characterController.enabled = false;
            transform.position = DormantPosition;
        }

        // Local player starts with controls disabled until ActivateAtPoint is called.
        if (IsLocalPlayer)
        {
            _playerMovementController.CanControl = false;
            playerLight.SetActive(false);
            nameTag.SetActive(false);
            Instance = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _isActivated.OnValueChanged -= OnActivatedChanged;
    }

    private void OnDestroy()
    {
        //if (PlayerHealth != null)
        //    PlayerHealth.OnDeath -= Die;
    }

    /// <summary>
    /// Teleports the player to the given spawn point and enables their controls.
    /// Can be called for both initial activation and subsequent repositions.
    /// SERVER ONLY.
    /// </summary>
    public void ActivateAtPoint(Transform spawnPoint, bool isOutside)
    {
        if (!IsServer) return;

        SetIsOutside(isOutside);

        if (IsOwner)
        {
            // Host: server is also the owner, so set position directly on the authoritative instance.
            _characterController.enabled = false;
            transform.SetPositionAndRotation(spawnPoint.position, spawnPoint.rotation);
            _characterController.enabled = true;
            _playerMovementController.SetCameraActive(true);
            _playerCameraController?.SetCameraActive(true);
            _playerMovementController.CanControl = true;
        }
        else
        {
            // Remote client: the NetworkTransform is owner-authoritative, so the server
            // cannot override the client's position. Send a ClientRpc directly to the
            // owning client so the teleport happens on the authoritative instance.
            var rpcParams = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
            };
            TeleportOwnerClientRpc(spawnPoint.position, spawnPoint.rotation, rpcParams);
        }

        if (!_isActivated.Value)
            _isActivated.Value = true;
    }

    /// <summary>
    /// Received only by the owning client. Sets position on the authoritative instance
    /// then enables cameras and controls so they go live after the teleport is applied.
    /// </summary>
    [ClientRpc]
    private void TeleportOwnerClientRpc(Vector3 position, Quaternion rotation, ClientRpcParams rpcParams = default)
    {
        if (_characterController != null) _characterController.enabled = false;
        transform.SetPositionAndRotation(position, rotation);
        if (_characterController != null) _characterController.enabled = true;

        if (IsLocalPlayer)
        {
            _playerMovementController.SetCameraActive(true);
            _playerCameraController?.SetCameraActive(true);
            _playerMovementController.CanControl = true;
        }
    }

    private void OnActivatedChanged(bool previous, bool current)
    {
        if (!current) return;

        // Proxy clients (non-owner, non-server) only need to re-enable their
        // CharacterController. Camera and controls are handled by TeleportOwnerClientRpc
        // for the local player and directly in ActivateAtPoint for the host.
        if (!IsServer && !IsLocalPlayer && _characterController != null)
            _characterController.enabled = true;
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
    /// Kills the local player: disables movement and interaction, then activates the ragdoll.
    /// </summary>
    public void Die()
    {
        return;

        CanControl = false;
        SetCanInteract(false);
        SetCanMove(false);
        DisableReticle();

        if (ragdollController != null)
            ragdollController.SetRagdollActive(true);
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
