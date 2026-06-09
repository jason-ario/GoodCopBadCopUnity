using UnityEngine;

public class DeathScreenUI : MonoBehaviour
{
    /// <summary>Called by the Spectate button's OnClick event in the Inspector.</summary>
    public void OnSpectateClicked()
    {
        gameObject.SetActive(false);
        PlayerInstance.Instance?.StartSpectating();
    }
}
