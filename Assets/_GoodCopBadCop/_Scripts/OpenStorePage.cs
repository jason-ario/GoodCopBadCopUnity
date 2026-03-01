using UnityEngine;

public class OpenSteamPage : MonoBehaviour
{
    // Replace with your actual Steam App ID
    [SerializeField] private string steamAppID = "YOUR_APP_ID";

    public void OpenSteam()
    {
        string url = "https://store.steampowered.com/app/" + steamAppID;
        Application.OpenURL(url);
    }
}