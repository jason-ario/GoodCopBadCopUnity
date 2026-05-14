using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerInstance : NetworkBehaviour
{
    public static PlayerInstance Instance;

    [SerializeField] private ScreenDamage screenDamage;
    [SerializeField] private GameObject playerLight;
    [SerializeField] private GameObject nameTag;
    [SerializeField] private RagdollController ragdollController;

    private readonly NetworkVariable<bool> _isOutside = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsOutside => _isOutside.Value;

    // Local-only cache updated immediately on the owning client, bypassing
    // the server round-trip so UI checks (e.g. ClientRpc notifications) see
    // the correct state even before the NetworkVariable replicates.
    private bool _isOutsideLocal;
    public bool IsOutsideLocal => _isOutsideLocal;
    
    public bool CanControl
    {
        get => _playerMovementController.CanControl;
        set => _playerMovementController.CanControl = value;
    } 

    private PlayerMovementController _playerMovementController;
    private PlayerInteractionController _playerInteractionController;
    public PlayerInteractionController PlayerInteractionController => _playerInteractionController;
    public PlayerRadiation PlayerRadiation { get; set; }
    public PlayerHealth PlayerHealth { get; set; }

    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = GetComponent<PlayerInteractionController>();
        PlayerHealth = GetComponent<PlayerHealth>();
        PlayerRadiation = GetComponent<PlayerRadiation>();

        //if (PlayerHealth != null)
           // PlayerHealth.OnDeath += Die;
    }

    private void OnDestroy()
    {
       // if (PlayerHealth != null)
           // PlayerHealth.OnDeath -= Die;
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
        // Mirror the value locally right away so ClientRpc handlers reading
        // IsOutsideLocal see the correct state without waiting for replication.
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

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (!IsLocalPlayer)
        {
            return;
        }
        
        playerLight.SetActive(false); 
        nameTag.SetActive(false);

        Instance = this;
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

    public void HurtPlayer()
    {
        screenDamage.CurrentHealth -= 1;
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
