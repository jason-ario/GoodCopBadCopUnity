using System;
using DG.Tweening;
using HighlightPlus;
using UnityEngine;

public class Radio : Interactable
{
    [SerializeField] AudioSource audioSource;
    private bool isOn;
    [SerializeField] private AudioSource _onSound;
    [SerializeField] private GameObject cinemachineCamera;
    [SerializeField] private Transform ikTarget;
    [SerializeField] private Vector3 ikOffset;
    private bool isUsingRadio;
    private PlayerMovementController _playerMovementController;
    private float frequency = 0;
    float maxFrequency = 1000;
    [SerializeField] private float maxArrowPos;
    [SerializeField] private float minArrowPos;
    [SerializeField] private Transform arrow;
    [SerializeField] private float moveArrowSpeed;

    
    public override void Interact(PlayerInteractionController playerInteractionController)
    {
        base.Interact(playerInteractionController);
        StartRadioInteraction(playerInteractionController);
    }

    public void StartRadioInteraction(PlayerInteractionController playerInteractionController)
    {
        isUsingRadio = true;
        cinemachineCamera.SetActive(true);
        
        playerInteractionController.GetComponent<PlayerMovementController>().SetCanControl(false);
        UIController.Instance.ShowBackButton(() => ExitRadio(playerInteractionController));
        GetComponent<HighlightEffect>().enabled = false;
        
        _playerMovementController = playerInteractionController.GetComponent<PlayerMovementController>();
        _playerMovementController.SetCanControl(false);
        playerInteractionController.enabled = false;

        _playerMovementController.LookAtTarget(transform);
        _playerMovementController.PlayerAnimationController.EnableHoldObjectMask();
        _playerMovementController.CameraTransform.DOMove(cinemachineCamera.transform.position, .5f); 
        _playerMovementController.CameraTransform.DORotate(cinemachineCamera.transform.rotation.eulerAngles, .5f);
        _playerMovementController.PlayerAnimationController.SetAnimBool("UsingRadio", true);
    }

    private void Update()
    {
        if (isUsingRadio)
        {
           ControlRadioFrequency();
        }
    }

    void ControlRadioFrequency()
    {
        float inputX = Input.GetAxis("Horizontal");
        frequency += Time.deltaTime * Input.GetAxis("Horizontal");
        if(frequency > maxFrequency) frequency = maxFrequency;
        if(frequency < 0) frequency = 0;

        _playerMovementController.PlayerAnimationController.SetAnimFloat("TuningRadio", inputX);
        
        Vector3 pos = arrow.position;
        pos.y += inputX * moveArrowSpeed * Time.deltaTime;
        if(pos.y > maxArrowPos) pos.y = maxArrowPos;
        if(pos.y < minArrowPos) pos.y = minArrowPos;
        arrow.position = pos;
    }
    

    void ExitRadio(PlayerInteractionController playerInteractionController)
    {
        cinemachineCamera.SetActive(false);
        
        playerInteractionController.GetComponent<PlayerMovementController>().SetCanControl(false);
        UIController.Instance.HideBackButton();
        GetComponent<HighlightEffect>().enabled = true;
        _playerMovementController.PlayerAnimationController.DisableHoldObjectMask();

        PlayerMovementController playerMovementController = playerInteractionController.GetComponent<PlayerMovementController>();

        playerMovementController.SetCanControl(true);
        playerMovementController.ResetCameraPos(false, .5f);
        playerInteractionController.enabled = true;
        _playerMovementController.PlayerAnimationController.SetAnimBool("UsingRadio", false);
    }
    
}
