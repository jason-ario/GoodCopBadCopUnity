using System;
using DG.Tweening;
using UnityEngine;

public class PC : Interactable
{
    [SerializeField] private GameObject computerCamera;
    [SerializeField] private Transform lookAtTarget;
    [SerializeField] private Transform standPos;
    bool pcActive = false;
    private PlayerInteractionController _player;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        player.playerMovementController.SetCanControl(false);
        UIController.Instance.ShowBackUI(true);
        
        player.playerMovementController.LookAtTarget(lookAtTarget.transform);
        player.transform.DOMove(standPos.position, .5f);
        player.transform.DORotate(standPos.rotation.eulerAngles, .5f);
        pcActive = true;
        _player = player;
    }

    private void Update()
    {
        if (pcActive)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                pcActive = false;
                UIController.Instance.ShowBackUI(false);
                _player.playerMovementController.SetCanControl(true);
            }
        }
    }
}
