using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor window that lets you switch the active NGO transport between
/// UnityTransport (LAN / local testing) and FacepunchTransport (Steam).
/// Open via menu: Tools > Transport Switcher  — or the toolbar shortcut.
/// </summary>
public class TransportSwitcherWindow : EditorWindow
{
    private const string MenuPath = "Tools/Transport Switcher";
    private const string PrefKey = "GoodCopBadCop_UseSteamTransport";

    // Colours used for the status pill.
    private static readonly Color SteamColor   = new Color(0.27f, 0.51f, 0.79f, 1f);
    private static readonly Color UnityColor   = new Color(0.22f, 0.65f, 0.35f, 1f);
    private static readonly Color LabelColor   = new Color(0.95f, 0.95f, 0.95f, 1f);

    [MenuItem(MenuPath)]
    public static void Open()
    {
        var window = GetWindow<TransportSwitcherWindow>(false, "Transport Switcher");
        window.minSize = new Vector2(260f, 140f);
        window.maxSize = new Vector2(400f, 140f);
        window.Show();
    }

    private void OnGUI()
    {
        var (networkManager, unity, facepunch) = FindTransports();

        EditorGUILayout.Space(10f);

        // ── Current status ─────────────────────────────────────────────────
        bool isSteamActive = networkManager != null &&
                             networkManager.NetworkConfig.NetworkTransport is FacepunchTransport;

        Color pillColor   = isSteamActive ? SteamColor : UnityColor;
        string pillLabel  = isSteamActive ? "Steam  (Facepunch)" : "Unity Transport  (LAN)";

        DrawStatusPill(pillColor, pillLabel);
        EditorGUILayout.Space(12f);

        // ── Toggle button ──────────────────────────────────────────────────
        EditorGUI.BeginDisabledGroup(networkManager == null || unity == null || facepunch == null);

        string buttonLabel = isSteamActive
            ? "Switch to Unity Transport"
            : "Switch to Steam Transport";

        if (GUILayout.Button(buttonLabel, GUILayout.Height(36f)))
        {
            SetTransport(networkManager, isSteamActive ? unity : facepunch);
            EditorPrefs.SetBool(PrefKey, !isSteamActive);
        }

        EditorGUI.EndDisabledGroup();

        // ── Warning when NetworkManager is missing ─────────────────────────
        if (networkManager == null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "No NetworkManager found in the loaded scenes.\nOpen a scene that contains one.",
                MessageType.Warning);
        }
        else if (unity == null || facepunch == null)
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "NetworkManager must have BOTH UnityTransport and FacepunchTransport components attached.",
                MessageType.Warning);
        }

        EditorGUILayout.Space(4f);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void DrawStatusPill(Color color, string label)
    {
        Rect rect = GUILayoutUtility.GetRect(0f, 28f, GUILayout.ExpandWidth(true));
        rect = new Rect(rect.x + 8f, rect.y, rect.width - 16f, rect.height);

        // Pill background
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, rect.height), color);

        // Label centred inside
        var style = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = LabelColor }
        };
        EditorGUI.LabelField(rect, label, style);
    }

    private static void SetTransport(NetworkManager networkManager, NetworkTransport transport)
    {
        Undo.RecordObject(networkManager, "Switch Network Transport");

        networkManager.NetworkConfig.NetworkTransport = transport;

        EditorUtility.SetDirty(networkManager);

        // Persist in the scene.
        var scene = networkManager.gameObject.scene;
        if (scene.IsValid())
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"[TransportSwitcher] Active transport → {transport.GetType().Name}");
    }

    /// <summary>Returns the first NetworkManager found in any loaded scene, plus the two transport components.</summary>
    private static (NetworkManager nm, UnityTransport unity, FacepunchTransport facepunch) FindTransports()
    {
        NetworkManager nm = FindFirstObjectByType<NetworkManager>();
        if (nm == null)
            return (null, null, null);

        var unity     = nm.GetComponent<UnityTransport>();
        var facepunch = nm.GetComponent<FacepunchTransport>();
        return (nm, unity, facepunch);
    }
}
