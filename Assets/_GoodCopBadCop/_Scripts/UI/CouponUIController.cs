using System.Collections;
using TMPro;
using UnityEngine;

public class CouponUIController : MonoBehaviour
{
    [SerializeField] Animator cashAnimation;
    [SerializeField] private TextMeshProUGUI cashTextUI;
    [SerializeField] private AudioClip addCashSound;
    
    public void PlayCashAnimation(int cashAmount)
    {
        cashTextUI.text = "+ " + cashAmount.ToString();
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
