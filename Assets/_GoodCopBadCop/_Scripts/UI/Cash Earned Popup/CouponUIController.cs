using System.Collections;
using TMPro;
using UnityEngine;

public class CouponUIController : MonoBehaviour
{
    [SerializeField] Animator cashAnimation;
    [SerializeField] private TextMeshProUGUI earnedCashInfoText;
    [SerializeField] private AudioClip addCashSound;
    
    public CouponUIController Instance;
    
    public void ShowEarnCashMessage(string message)
    {
        earnedCashInfoText.text = message;
        cashAnimation.gameObject.SetActive(true);
        SFXController.Instance.Play(addCashSound);
        StartCoroutine(WaitAndFinishAnimation());
    }

    IEnumerator WaitAndFinishAnimation()
    {
        yield return new WaitForSeconds(1f);
        cashAnimation.gameObject.SetActive(false);
    }
}
