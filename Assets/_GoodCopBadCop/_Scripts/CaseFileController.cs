using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

public class CaseFileController : Interactable
{
    [SerializeField] Transform standingPosition;
    [SerializeField] private float lerpDuration = 1;
    [SerializeField] private float cameraLookSharpness = 8;
    bool lookingAtCaseFile = false;
    private PlayerMovementController _playerMovementController;
    [SerializeField] Animator animator;
    [SerializeField] private Transform lookPos;
    [SerializeField] private GameObject caseFileUI;
    [SerializeField] SuspectData suspect;
    
    public override void Interact(PlayerInteractionController player)
    {
        PlayerMovementController playerMovementController = player.GetComponent<PlayerMovementController>();
        _playerMovementController = playerMovementController;
        playerMovementController.SetCanControl(false);
        animator.SetBool("Open", true);
        player.transform.DOMove(standingPosition.position, lerpDuration);
        player.transform.DORotateQuaternion(standingPosition.rotation, lerpDuration);
        lookingAtCaseFile = true;
        caseFileUI.SetActive(true);
    }
    
    private void Update()
    {
        if (lookingAtCaseFile)
        {
            var camTransform = _playerMovementController.CameraTransform.transform;

            Vector3 toTarget = lookPos.position - camTransform.position;
            if (toTarget.sqrMagnitude < 0.000001f) return;

            Quaternion targetRot = Quaternion.LookRotation(toTarget, Vector3.up);

            // Frame-rate independent easing factor in [0..1]
            float t = 1f - Mathf.Exp(-cameraLookSharpness * Time.deltaTime);

            camTransform.rotation = Quaternion.Slerp(camTransform.rotation, targetRot, t);
        }
    }

    public void StartInterrogation()
    {
        CloseCaseFile();
        SceneContextController.Instance.OnLevelSelected(); 
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCanControl(true);
        UIController.Instance.CloseLevelSelectUI();
    }

    public void CloseCaseFile()
    {
        _playerMovementController.transform.DOKill();
        animator.SetBool("Open", false);
        lookingAtCaseFile = false;
        _playerMovementController.SetCanControl(true);
        caseFileUI.SetActive(false);
    }
}
