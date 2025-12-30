using System;
using Unity.Netcode;
using UnityEngine;

public class DebugConsole : MonoBehaviour
{
    public bool skipMainMenu;

    private void Start()
    {
        if (skipMainMenu)
        {
            NetworkManager.Singleton.StartHost();
            GameManager.Instance.TryStartGame(true);
        }
    }
}
