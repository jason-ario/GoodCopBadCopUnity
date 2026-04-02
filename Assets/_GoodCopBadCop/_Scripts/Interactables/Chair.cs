using System;
using DG.Tweening;
using UnityEngine;

public class Chair : Interactable
{
    [SerializeField] private Transform sitPos;
    public Transform SitPos => sitPos;
    [SerializeField] private Transform standingPos;
    public Transform StandingPos => standingPos;
    [SerializeField] float sitDuration = .5f;
    public bool canMoveWhileSeated = false;
    public bool canRotateWhileSeated = false;

    private void Start()
    {
        standingPos.transform.parent = null;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        Sit(player);
    }

    void Sit(PlayerInteractionController player)
    {
        UIController.Instance.ShowBackUI(true);
        player.GetComponent<PlayerMovementController>().Sit(this);
        player.transform.DOMove(sitPos.position, sitDuration);
        player.transform.DORotate(sitPos.eulerAngles, sitDuration).OnComplete(() => OnSeated(player.transform));
    }

    public void SitImmediate(PlayerInteractionController player)
    {
        player.GetComponent<PlayerMovementController>().Sit(this);
        player.transform.position = sitPos.position;
        player.transform.rotation = sitPos.rotation;
        OnSeated(player.transform);
    }

    void OnSeated(Transform player)
    {
        transform.parent = player.transform;
        player.GetComponent<PlayerMovementController>().SetCanLook(canRotateWhileSeated);
        player.GetComponent<PlayerMovementController>().SetCanMove(canMoveWhileSeated);
    }
}
