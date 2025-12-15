using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;

public class CaseFileController : MonoBehaviour, IInteractable
{
    [SerializeField] Transform standingPosition;
    [SerializeField] private float lerpDuration = 1;
    [SerializeField] private float cameraLookSharpness = 8;
    bool lookingAtCaseFile = false;
    private PlayerMovementController _playerMovementController;
    [SerializeField] Animator animator;
    [SerializeField] private Transform lookPos;
    
    public void Interact(PlayerInteractionController player)
    {
        PlayerMovementController playerMovementController = player.GetComponent<PlayerMovementController>();
        _playerMovementController = playerMovementController;
        playerMovementController.SetCanControl(false);
        player.transform.DOMove(standingPosition.position, lerpDuration).OnComplete(() => animator.SetBool("Open", true));
        player.transform.DORotateQuaternion(standingPosition.rotation, lerpDuration);
        lookingAtCaseFile = true;
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
}
