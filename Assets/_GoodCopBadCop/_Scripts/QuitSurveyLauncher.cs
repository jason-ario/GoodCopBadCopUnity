using UnityEngine;

/// <summary>
/// Opens the playtest survey link in the user's browser whenever the application quits,
/// for whatever reason (Quit button, closing the window, Alt+F4, task kill via OS signal, etc.).
/// Self-installs at startup, so no manual scene setup is required.
/// </summary>
public class QuitSurveyLauncher : MonoBehaviour
{
    private const string SurveyUrl = "https://tinyurl.com/UncannyValleyPlaytestCPTSurvey";

    private static bool _surveyOpened;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Install()
    {
        if (FindAnyObjectByType<QuitSurveyLauncher>() != null)
        {
            return;
        }

        var go = new GameObject("QuitSurveyLauncher");
        go.AddComponent<QuitSurveyLauncher>();
        DontDestroyOnLoad(go);
    }

    private void OnApplicationQuit()
    {
        OpenSurveyOnce();
    }

    private static void OpenSurveyOnce()
    {
        if (_surveyOpened)
        {
            return;
        }

        _surveyOpened = true;
        Application.OpenURL(SurveyUrl);
    }
}
