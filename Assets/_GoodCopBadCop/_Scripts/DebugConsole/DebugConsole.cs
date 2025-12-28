using System;
using Unity.Netcode;
using UnityEngine;

public class DebugConsole : MonoBehaviour
{
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F5))
        {
            SkipMainMenu();
        }
    }

    public void SkipMainMenu()
    {
        NetworkManager.Singleton.StartHost();
        GameManager.Instance.TryStartGame();
    }
}
