using UnityEngine;

public class IntroTutorialManager : MonoBehaviour
{
    [SerializeField] private Chair[] chairs;
    [SerializeField] private PickableItemData coffee;
    public void StartIntroTutorial()
    {
        //Force player into seat
        Debug.Log("Sit in seat");
        int chairIndex = (int)PlayerInstance.Instance.OwnerClientId;
        Chair chair = chairs[chairIndex];
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.transform.position = chair.SitPos.position;
        PlayerInstance.Instance.transform.rotation = chair.SitPos.rotation;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().PickUpObject(coffee);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCantSitOrStand(false);
        chair.SitImmediate(PlayerInstance.Instance.GetComponent<PlayerInteractionController>());
    }
}
