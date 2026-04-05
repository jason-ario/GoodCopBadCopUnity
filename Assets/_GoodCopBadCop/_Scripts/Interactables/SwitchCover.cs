using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class SwitchCover : Interactable
{
    [SerializeField] Animator anim;
    public NetworkVariable<bool> switchCoverOpen;
    [SerializeField] private BoxCollider switchBoxCollider;
    [SerializeField] AudioSource audioSource; 
    [SerializeField] Transform ikTarget;

    protected override void Awake()
    {
        base.Awake();
        switchCoverOpen.OnValueChanged += OnSwitchOpenValueChanged;
    }

    private void OnSwitchOpenValueChanged(bool previousValue, bool newValue)
    {
        interactText = newValue ? "Close Switch" : "Open Switch";
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        if (switchCoverOpen.Value)
        {
            player.playerAnimationController.SetAnimTrigger("CloseSwitch");
            switchBoxCollider.enabled = false;
            switchCoverOpen.Value = false;
        }
        else
        {
            player.playerAnimationController.SetAnimTrigger("OpenSwitch");
            switchBoxCollider.enabled = true;
            switchCoverOpen.Value = true;
        }
        
        StartCoroutine(WaitAndOpenSwitch(player));
    }

    IEnumerator WaitAndOpenSwitch(PlayerInteractionController player)
    {
        player.playerAnimationController.EnableRightArmMask();
        yield return new WaitForSeconds(0.3f);
        anim.SetBool("SwitchOpen", switchCoverOpen.Value);
        audioSource.Play();
        yield return new WaitForSeconds(0.5f);
        player.playerAnimationController.DisableRightArmMask();
    }

    public void Reset()
    {
        switchCoverOpen.Value = false;
        switchBoxCollider.enabled = false;
        switchCoverOpen.Value = false;
        anim.SetBool("SwitchOpen", false);
    }
}
