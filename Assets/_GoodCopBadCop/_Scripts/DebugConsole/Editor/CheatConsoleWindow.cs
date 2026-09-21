using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window version of the F12 in-game cheat console — trigger the same cheats
/// with a click in the Editor instead of switching focus to the Game view and pressing
/// F12. Entries come from the shared <see cref="CheatConsoleActions"/> registry, so
/// this list always matches the in-game overlay; add new cheats there, not here.
/// Requires an active Play-mode session (cheats call into runtime singletons such as
/// <see cref="DebugConsole"/>.Instance) — buttons are disabled otherwise.
/// </summary>
public class CheatConsoleWindow : EditorWindow
{
    private (string Label, Action Callback)[] _cheats;
    private Vector2 _scroll;

    [MenuItem("Good Cop Bad Cop/Cheat Console")]
    public static void ShowWindow()
    {
        var window = GetWindow<CheatConsoleWindow>("Cheat Console");
        window.minSize = new Vector2(420f, 320f);
    }

    private void OnEnable()
    {
        _cheats = CheatConsoleActions.BuildCheats().ToArray();
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private void OnDisable()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
    }

    private void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        Repaint();
    }

    private void OnGUI()
    {
        bool isPlaying = EditorApplication.isPlaying;

        EditorGUILayout.Space(4f);
        EditorGUILayout.LabelField("Cheat Console", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            isPlaying
                ? "Click a cheat to trigger it immediately in the running game."
                : "Enter Play Mode to use these cheats — they call into runtime systems that only exist while the game is running.",
            isPlaying ? MessageType.Info : MessageType.Warning);
        EditorGUILayout.Space(6f);

        using (new EditorGUI.DisabledScope(!isPlaying))
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            foreach (var (label, callback) in _cheats)
            {
                if (GUILayout.Button(label, GUILayout.Height(28f)))
                {
                    try
                    {
                        callback();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"[CheatConsoleWindow] Cheat '{label}' failed: {e}");
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }
    }
}
