using UnityEngine;
using UnityEditor;

[InitializeOnLoad]
public class RunInBackground
{
    static RunInBackground()
    {
        EditorApplication.playModeStateChanged += Running;
    }

    private static void Running(PlayModeStateChange obj)
    {
        if (EditorApplication.isPlaying)
        {
            Application.runInBackground = true;
        }
    }
}