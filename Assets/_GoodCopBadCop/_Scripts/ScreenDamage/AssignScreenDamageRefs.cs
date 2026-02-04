using UnityEngine;

public class AssignScreenDamageRefs : MonoBehaviour
{
    [SerializeField] private ScreenDamage _screenDamage;

    void Awake()
    {
        _screenDamage.bloodyFrame = UIController.Instance.ScreenDamageCanvas.bloodyFrame;
        _screenDamage.blurImage = UIController.Instance.ScreenDamageCanvas.blurImage;
    }
}
