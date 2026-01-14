using Unity.Netcode;
using UnityEngine;

public class PurchaseLocker : Interactable
{
    [SerializeField] private LockerPrice price;
    [SerializeField] private Animator anim;
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<bool> isPurchased = new NetworkVariable<bool>(false);
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lockerOpenSound;
    [SerializeField] private AudioClip lockerCloseSound;

    protected override void Awake()
    {
        base.Awake();
        price.SetPrice(50);
    }
    
    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += (oldValue, newValue) =>
        {
            anim.SetBool("Open", newValue);
            if (newValue == false)
            {
                audioSource.PlayOneShot(lockerCloseSound);
            }
            else
            {
                audioSource.PlayOneShot(lockerOpenSound);
            }
        };
        
        isPurchased.OnValueChanged += (oldValue, newValue) =>
        {
            isOpen.Value = true;
            price.gameObject.SetActive(false);
        };
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleMiniLockerServerRpc()
    {
        isOpen.Value = !isOpen.Value;
    }

    public override void Interact(PlayerInteractionController player)
    {
        //Purchase Item
        if (isPurchased.Value == false)
        {
            OnPurchase();
        }
        else
        {
            ToggleMiniLockerServerRpc();
        }
    }

    void OnPurchase()
    {
        isOpen.Value = true;
        isPurchased.Value = true;
    }

    protected override void OnHighlight()
    {
        base.OnHighlight();

        if (isPurchased.Value)
        {
            price.gameObject.SetActive(false);
        }
        else
        {
            price.gameObject.SetActive(true);
        }
    }

    protected override void OnStopHighlight()
    {
        base.OnStopHighlight();
        price.gameObject.SetActive(false);
    }



    [ServerRpc(RequireOwnership = false)]
    public void CloseServerRpc()
    {
        isOpen.Value = false;
    }
}
