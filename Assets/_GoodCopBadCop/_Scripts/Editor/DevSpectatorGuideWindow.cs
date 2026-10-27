using System.Collections.Generic;
using System.Linq;
using Steamworks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// In-editor guide for the hidden developer spectator mode (DevSessionBrowser), with a live
/// status panel: build channel / Steam AppId, the SteamID whitelist and, in Play Mode, the
/// local Steam user plus a button to open the Dev Session Browser.
/// </summary>
public class DevSpectatorGuideWindow : EditorWindow
{
    private const string ConfigScriptPath = "Assets/_GoodCopBadCop/_Scripts/Networking/DevSpectator/DevSpectatorConfig.cs";

    private struct Section
    {
        public string title;
        public string body;
    }

    private static readonly List<Section> Sections = new List<Section>
    {
        new Section
        {
            title = "1. What it does",
            body =
                "A whitelisted developer can list every public Steam lobby for the current Steam app and join any of them as an <b>invisible spectator</b>: " +
                "no player object is spawned, you never appear in the lobby list or player counts, and you can cycle through the real players' cameras.\n" +
                "Every lobby reserves 1 extra Steam slot for spectators, so even a full 3/3 lobby can be watched."
        },
        new Section
        {
            title = "2. One-time setup (whitelist)",
            body =
                "1. Add your SteamID64 to <b>DevSpectatorConfig.DevSteamIds</b> (button below). Don't know it? Enter Play Mode, press <b>Ctrl+Shift+O</b> and use <b>Copy my SteamID</b>.\n" +
                "2. <b>Ship a build containing the whitelist.</b> The <i>host's</i> game decides whether to let you in, using its own copy of the list. Sessions hosted on older builds will reject you.\n" +
                "3. The host also checks that the SteamID you claim matches your real, Steam-verified connection, so nobody can pretend to be a whitelisted ID."
        },
        new Section
        {
            title = "3. Spectating a session",
            body =
                "1. Run the game on the <b>same build channel</b> as the players (Playtest / Demo / Release). Lobby lists are per Steam AppId, so you can't see other channels' lobbies.\n" +
                "2. Make sure Steam is running and the <b>Facepunch (Steam) transport</b> is active. LAN/UnityTransport is not supported.\n" +
                "3. Anywhere in the game press <b>Ctrl+Shift+O</b> to open the Dev Session Browser.\n" +
                "4. Press <b>Refresh</b>. Each row shows host name, visible player count, join code and lobby ID.\n" +
                "5. Press <b>Spectate</b> on a row. You leave your current lobby, join theirs hidden and connect (20 s timeout)."
        },
        new Section
        {
            title = "4. While spectating",
            body =
                "- <b>LMB / RMB</b> (or <b>Next > / < Prev</b> in the window) switches between players. The camera retargets automatically when the watched player dies or leaves.\n" +
                "- <b>Hide HUD</b> keeps the player UI closed (on by default).\n" +
                "- Mouse cycling is paused while the window is open. Press Ctrl+Shift+O again to close it.\n" +
                "- <b>Leave session</b> exits the lobby and reloads the scene back to the main menu. If the host ends the game you are dropped out automatically."
        },
        new Section
        {
            title = "5. Who can open it",
            body =
                "- <b>Builds:</b> only Steam accounts in the whitelist. For everyone else Ctrl+Shift+O does nothing.\n" +
                "- <b>Editor:</b> always opens, so you can read your SteamID, but the lobby list and Spectate stay locked unless you're whitelisted."
        },
        new Section
        {
            title = "6. Troubleshooting",
            body =
                "- <b>No sessions listed:</b> wrong build channel, Steam not running, or the lobby was created before its data was searchable (Refresh again).\n" +
                "- <b>Join failed / timed out:</b> the host's build doesn't include your SteamID, or the host's connection rejected you. Look in the host's log for <i>SpectatorIdentityMismatch</i> or <i>Could not verify spectator identity</i> (the second can appear after an NGO upgrade, because the check reads NGO internals).\n" +
                "- <b>\"Spectating requires the Facepunch transport\":</b> switch transport back to Facepunch (TransportSwitcher).\n" +
                "- <b>Black screen / nobody to watch:</b> no living, spawned players yet (e.g. still in the lobby). The window says \"Watching: nobody\"."
        },
        new Section
        {
            title = "7. Code map",
            body =
                "- <b>DevSpectatorConfig</b>: whitelist, reserved slots, connection payload, identity verification.\n" +
                "- <b>DevSpectatorRegistry</b>: server-side set of spectator clients. Gameplay code that counts or loops over players must use <b>PlayerClientCount / PlayerClientIds</b>.\n" +
                "- <b>DevSessionBrowser</b>: the Ctrl+Shift+O window, spectator camera and HUD hiding.\n" +
                "- <b>LobbyManager.JoinLobbyAsDevSpectator / ApproveConnection</b>: join and host approval."
        }
    };

    private Vector2 _scroll;
    private GUIStyle _body;
    private GUIStyle _title;

    [MenuItem("Tools/Dev Spectator Guide")]
    public static void Open()
    {
        var w = GetWindow<DevSpectatorGuideWindow>("Dev Spectator Guide");
        w.minSize = new Vector2(440, 380);
        w.Show();
    }

    private void OnInspectorUpdate()
    {
        if (EditorApplication.isPlaying)
            Repaint();
    }

    private void OnGUI()
    {
        if (_body == null)
        {
            _body = new GUIStyle(EditorStyles.label) { wordWrap = true, richText = true, padding = new RectOffset(8, 8, 2, 8) };
            _title = new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, padding = new RectOffset(4, 4, 8, 2) };
        }

        _scroll = EditorGUILayout.BeginScrollView(_scroll);
        GUILayout.Label("Dev Spectator Guide", new GUIStyle(EditorStyles.boldLabel) { fontSize = 16 });

        DrawStatus();

        foreach (var s in Sections)
        {
            GUILayout.Label(s.title, _title);
            GUILayout.Label(s.body, _body);
        }
        EditorGUILayout.EndScrollView();
    }

    private void DrawStatus()
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        GUILayout.Label("Status", EditorStyles.boldLabel);

        var settings = GameSettings.Instance;
        EditorGUILayout.LabelField("Build channel",
            settings != null ? $"{settings.Channel} (Steam AppId {settings.SteamAppId})" : "GameSettings not found");

        var ids = DevSpectatorConfig.DevSteamIds.ToList();
        EditorGUILayout.LabelField("Whitelisted SteamIDs", ids.Count == 0 ? "none: spectating is disabled" : ids.Count.ToString());
        foreach (ulong id in ids)
            EditorGUILayout.SelectableLabel(id.ToString(), GUILayout.Height(EditorGUIUtility.singleLineHeight));

        if (EditorApplication.isPlaying)
        {
            bool steamValid = SteamClient.IsValid;
            EditorGUILayout.LabelField("Local Steam user",
                steamValid ? $"{SteamClient.Name} ({SteamClient.SteamId.Value})" : "Steam not initialized");
            EditorGUILayout.LabelField("Whitelisted", DevSpectatorConfig.IsLocalDev ? "Yes" : "No");
            EditorGUILayout.LabelField("Spectating", DevSpectatorRegistry.IsLocalSpectating ? "Yes (hidden)" : "No");
        }
        else
        {
            EditorGUILayout.HelpBox("Enter Play Mode to see your Steam user and open the Dev Session Browser.", MessageType.None);
        }

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Edit whitelist (DevSpectatorConfig.cs)"))
        {
            var script = AssetDatabase.LoadAssetAtPath<MonoScript>(ConfigScriptPath);
            if (script != null) AssetDatabase.OpenAsset(script);
            else Debug.LogWarning($"[DevSpectatorGuide] Could not find {ConfigScriptPath}");
        }

        GUI.enabled = EditorApplication.isPlaying && DevSessionBrowser.Instance != null;
        if (GUILayout.Button(DevSessionBrowser.IsOpen ? "Close Dev Session Browser" : "Open Dev Session Browser"))
            DevSessionBrowser.Instance.Toggle();
        GUI.enabled = true;
        GUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }
}
