using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ToolsLocker : Interactable
{
    [SerializeField] private Animator anim;
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    [SerializeField] private PurchaseLocker[] miniLockers;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lockerOpenSound;
    [SerializeField] private AudioClip lockerCloseSound;
    [SerializeField] private Transform lookTarget;
    [SerializeField] private GameObject[] decor;
    Coroutine closeCoroutine;
    
    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += (oldValue, newValue) =>
        {
            anim.SetBool("Open", newValue);

            if (newValue == true)
            {
                foreach (var decoration in decor)
                {
                    decoration.SetActive(newValue);
                }
            }
        };
    }

    [ContextMenu("Open")]
    public void ForceOpen()
    {
        OpenLockerServerRpc();
    }

    public override void Interact(PlayerInteractionController player)
    {
        Debug.Log("Toggle Tool Locker");
        UIController.Instance.OpenToolShop(lookTarget);
        
        OpenLockerServerRpc();
    }

    [ServerRpc]
    public void CloseLockerServerRpc()
    {
        isOpen.Value = false;

        audioSource.PlayOneShot(lockerCloseSound);
        foreach (var miniLocker in miniLockers)
        {
            miniLocker.CloseServerRpc();
        }

        closeCoroutine = StartCoroutine(CloseLocker());
    }

    IEnumerator CloseLocker()
    {
        yield return new WaitForSeconds(3f);
        if (!isOpen.Value)
        {
            foreach (var decoration in decor)
            {
                decoration.SetActive(false);
            }
        }
    }
    
    
    [ServerRpc]
    public void OpenLockerServerRpc()
    {
        isOpen.Value = true;
        StopCoroutine(closeCoroutine);
        audioSource.PlayOneShot(lockerOpenSound);
    }
}
