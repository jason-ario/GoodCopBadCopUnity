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


    private void Start()
    {
        standingPos.transform.parent = null;
    }

    public override void Interact(PlayerInteractionController player)
    {
        Sit(player);
    }

    void Sit(PlayerInteractionController player)
    {
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
        player.GetComponent<PlayerMovementController>().SetCanLook(true);
    }
}
