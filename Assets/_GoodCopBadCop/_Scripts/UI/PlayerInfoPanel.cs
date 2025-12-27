using TMPro;
using UnityEngine;

public class PlayerInfoPanel : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI userName;

    public void PopulateInfo(string UserName)
    {
        userName.text = UserName;
    }
}
