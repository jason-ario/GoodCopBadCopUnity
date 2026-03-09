using UnityEngine;

public class IntroTutorialManager : MonoBehaviour
{
    [SerializeField] private Chair[] _chairs;
    public void StartIntroTutorial()
    {
        //Force player into seat
        Debug.Log("Sit in seat");
        int chairIndex = (int)PlayerInstance.Instance.OwnerClientId;
        Chair chair = _chairs[chairIndex];
        PlayerInstance.Instance.transform.position = chair.SitPos.position;
        PlayerInstance.Instance.transform.rotation = chair.SitPos.rotation;
        chair.SitImmediate(PlayerInstance.Instance.GetComponent<PlayerInteractionController>());
    }
}
