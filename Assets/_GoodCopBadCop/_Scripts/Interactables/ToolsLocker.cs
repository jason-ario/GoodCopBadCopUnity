using System;
using System.Collections;
using Unity.Netcode;
using UnityEngine;

public class ToolsLocker : Interactable
{
    /// <summary>
    /// Fired on all clients (including server) whenever any tool locker transitions to open.
    /// Used by tutorial systems to detect when a player opens the locker.
    /// </summary>
    public static event Action OnAnyLockerOpened;
    [SerializeField] private Animator anim;
    private NetworkVariable<bool> isOpen = new NetworkVariable<bool>(false);
    private NetworkVariable<int> viewerCount = new NetworkVariable<int>(0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    [SerializeField] private PurchaseLocker[] miniLockers;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip lockerOpenSound;
    [SerializeField] private AudioClip lockerCloseSound;
    [SerializeField] private Transform lookTarget;
    [SerializeField] private GameObject[] decor;
    [SerializeField] private ToolLockerDiegeticController _diegeticController;
    Coroutine closeCoroutine;

    public override void OnNetworkSpawn()
    {
        isOpen.OnValueChanged += (oldValue, newValue) =>
        {
            anim.SetBool("Open", newValue);

            if (newValue)
            {
                OnAnyLockerOpened?.Invoke();
                audioSource.PlayOneShot(lockerOpenSound);

                foreach (var decoration in decor)
                {
                    decoration.SetActive(true);
                }
            }
            else
            {
                audioSource.PlayOneShot(lockerCloseSound);
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

        if (_diegeticController != null)
            _diegeticController.Open(player, this);
        else
            UIController.Instance.OpenToolShop(lookTarget, this);

        OpenLockerServerRpc();
    }

    /// <summary>Called by each local client when they close the tool shop UI. Decrements the viewer count and closes the locker when no viewers remain.</summary>
    [ServerRpc(RequireOwnership = false)]
    public void NotifyPlayerClosedServerRpc()
    {
        viewerCount.Value = Mathf.Max(0, viewerCount.Value - 1);

        if (viewerCount.Value == 0)
        {
            CloseLockerInternal();
        }
    }

    [ServerRpc(RequireOwnership = false)]
    public void CloseLockerServerRpc()
    {
        CloseLockerInternal();
    }

    private void CloseLockerInternal()
    {
        isOpen.Value = false;
        viewerCount.Value = 0;

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

    [ServerRpc(RequireOwnership = false)]
    public void OpenLockerServerRpc()
    {
        if (closeCoroutine != null)
        {
            StopCoroutine(closeCoroutine);
            closeCoroutine = null;
        }

        viewerCount.Value++;
        isOpen.Value = true;
    }
}
