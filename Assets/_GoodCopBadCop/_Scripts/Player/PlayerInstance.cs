using System;
using UnityEngine;

public class PlayerInstance : MonoBehaviour
{
    public static PlayerInstance Instance;

    public bool CanControl => _playerMovementController.CanControl;

    private PlayerMovementController _playerMovementController;
    
    private void Awake()
    {
        Instance = this;
        _playerMovementController = GetComponent<PlayerMovementController>();
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
