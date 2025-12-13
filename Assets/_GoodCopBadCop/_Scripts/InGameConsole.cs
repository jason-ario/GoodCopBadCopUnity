using UnityEngine;
using System.Collections.Generic;

public class InGameConsole : MonoBehaviour
{
    public KeyCode toggleKey = KeyCode.BackQuote; // ` key
    public int maxLogs = 200;

    private bool showConsole = false;
    private Vector2 scroll;
    private List<string> logs = new List<string>();

    void OnEnable()
    {
        Application.logMessageReceived += HandleLog;
    }

    void OnDisable()
    {
        Application.logMessageReceived -= HandleLog;
    }

    void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            showConsole = !showConsole;
    }

    void HandleLog(string logString, string stackTrace, LogType type)
    {
        string entry = $"[{type}] {logString}";
        logs.Add(entry);

        if (logs.Count > maxLogs)
            logs.RemoveAt(0);
    }

    void OnGUI()
    {
        if (!showConsole) return;

        GUI.Box(new Rect(0, 0, Screen.width, Screen.height / 2), "Console");

        GUILayout.BeginArea(new Rect(10, 30, Screen.width - 20, Screen.height / 2 - 40));
        scroll = GUILayout.BeginScrollView(scroll);

        foreach (var log in logs)
        {
            GUILayout.Label(log);
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}