using Unity.Netcode;
using UnityEngine;

public class IntroTutorialManager : MonoBehaviour
{
    [SerializeField] private Chair[] chairs;
    [SerializeField] private Chair singlePlayerChair;
    [SerializeField] private PickableItemData coffee;
    
    public void StartIntroTutorial()
    {
        //Force player into seat
        int chairIndex = (int)PlayerInstance.Instance.OwnerClientId;
        Chair chair = chairs[chairIndex];
        
        bool isSinglePlayer = GameManager.Instance.IsSinglePlayer;
        if (isSinglePlayer) chair = singlePlayerChair;

        chair.gameObject.SetActive(true);
        
        foreach (var c in chairs)
        {
            c.gameObject.SetActive(!isSinglePlayer);
        }
        
        PlayerInstance.Instance.SetCanInteract(false);
        PlayerInstance.Instance.transform.position = chair.SitPos.position;
        PlayerInstance.Instance.transform.rotation = chair.SitPos.rotation;
        PlayerInstance.Instance.GetComponent<PlayerPickupController>().PickUpObject(coffee);
        PlayerInstance.Instance.GetComponent<PlayerMovementController>().SetCantSitOrStand(false);
        chair.SitImmediate(PlayerInstance.Instance.GetComponent<PlayerInteractionController>());
    }
}
