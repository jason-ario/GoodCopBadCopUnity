using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerInstance : NetworkBehaviour
{
    public static PlayerInstance Instance;

    [SerializeField] private ScreenDamage screenDamage;
    [SerializeField] private bool isOutside;
    public bool IsOutside => isOutside;
    [SerializeField] private GameObject playerLight;
    
    public bool CanControl
    {
        get => _playerMovementController.CanControl;
        set => _playerMovementController.CanControl = value;
    } 

    private PlayerMovementController _playerMovementController;
    private PlayerInteractionController _playerInteractionController;
    public PlayerInteractionController PlayerInteractionController => _playerInteractionController;
    public PlayerRadiation PlayerRadiation { get; set; }

    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
        _playerInteractionController = GetComponent<PlayerInteractionController>();
        PlayerRadiation = GetComponent<PlayerRadiation>();
        
        Instance = this;

    }

    public void SetIsOutside(bool value)
    {
        isOutside = value;
        playerLight.SetActive(value);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (!IsLocalPlayer)
        {
            return;
        }
        
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
        throw new NotImplementedException();
    }
}
