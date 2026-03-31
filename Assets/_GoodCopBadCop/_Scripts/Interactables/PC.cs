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
    [SerializeField] private SimpleCanvasCursorFromMouseDelta _virtualCanvasCursor;

    [Header("Screens")] 
    [SerializeField] private GameObject[] screens;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        
        player.playerMovementController.SetCanControl(false);
        player.SetCanInteract(false, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        UIController.Instance.ShowBackUI(true);
        
        player.playerMovementController.LookAtTarget(lookAtTarget.transform);
        player.transform.DOMove(standPos.position, .5f);
        player.transform.DORotate(standPos.rotation.eulerAngles, .5f);
        pcActive = true;
        _player = player;
        _virtualCanvasCursor.enabled = true;
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
                _player.SetCanInteract(true, "");
            }
        }
    }

    public void OpenScreen(GameObject screen)
    {
        for (int i = 0; i < screens.Length; i++)
        {
            screens[i].SetActive(false);
            if (screens[i] == screen) screens[i].SetActive(true);
        }
        
        _virtualCanvasCursor.SetScreenContent(screen.transform);
    }
}
