using UnityEngine;

/// <summary>
/// Opens the game's Steam store page in the user's browser whenever the application quits,
/// for whatever reason (Quit button, closing the window, Alt+F4, task kill via OS signal, etc.).
/// Gated by <see cref="GameSettings.OpenStorePageOnGameExit"/>.
/// Self-installs at startup, so no manual scene setup is required.
/// </summary>
/// <remarks>Class name kept as QuitSurveyLauncher for history; it no longer opens the survey.</remarks>
public class QuitSurveyLauncher : MonoBehaviour
{
    private static bool _pageOpened;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<QuitSurveyLauncher>() != null)
        {
            return;
        }

        var go = new GameObject("QuitStorePageLauncher");
        go.AddComponent<QuitSurveyLauncher>();
        DontDestroyOnLoad(go);
    }

    private void OnApplicationQuit()
    {
        if (GameSettings.Instance.OpenStorePageOnGameExit)
        {
            OpenStorePageOnce();
        }
    }

    private static void OpenStorePageOnce()
    {
        if (_pageOpened)
        {
            return;
        }

        _pageOpened = true;
        Application.OpenURL(GameSettings.SteamStorePageUrl);
    }
}
