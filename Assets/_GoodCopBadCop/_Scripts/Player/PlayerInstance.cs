using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerInstance : NetworkBehaviour
{
    public static PlayerInstance Instance;

    public bool CanControl => _playerMovementController.CanControl;

    private PlayerMovementController _playerMovementController;
    
    private void Awake()
    {
        _playerMovementController = GetComponent<PlayerMovementController>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        
        if (IsLocalPlayer == false)
        {
            return;
        }
        
        Instance = this;
    }

    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
