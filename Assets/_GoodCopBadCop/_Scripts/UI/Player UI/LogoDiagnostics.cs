using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// TEMPORARY diagnostics for the "logo missing after returning to the main menu" bug.
/// After every scene load, snapshots the state of every LogoMaterialController (including inactive
/// ones) at a few points in time, independently of the logo's own scripts. Search logs for [LogoDiag].
/// Delete this file once the bug is fixed.
/// </summary>
public class LogoDiagnostics : MonoBehaviour
{
    private static readonly int BurnProgressId = Shader.PropertyToID("_BurnProgress");
    private static LogoDiagnostics _instance;
    private static int _loadCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Init()
    {
        if (_instance != null) return;
        var go = new GameObject("[LogoDiagnostics]");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<LogoDiagnostics>();
        SceneManager.sceneLoaded += (scene, mode) => _instance?.OnSceneLoaded(scene, mode);
        _instance.OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        int load = ++_loadCount;
        Debug.Log($"[LogoDiag] Scene loaded #{load}: '{scene.name}' ({mode}) frame={Time.frameCount} timeScale={Time.timeScale} audioPaused={AudioListener.pause}");
        StartCoroutine(Snapshots(load));
    }

    private IEnumerator Snapshots(int load)
    {
        yield return null;
        Snapshot(load, "1 frame");
        float loadTime = Time.realtimeSinceStartup;
        foreach (float t in new[] { 1f, 3f, 6f })
        {
            while (Time.realtimeSinceStartup - loadTime < t)
                yield return null;
            Snapshot(load, $"{t}s");
        }
    }

    private static void Snapshot(int load, string when)
    {
        var logos = FindObjectsByType<LogoMaterialController>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        if (logos.Length == 0)
        {
            Debug.Log($"[LogoDiag] load#{load} @{when}: no LogoMaterialController found.");
            return;
        }

        foreach (var logo in logos)
        {
            var sb = new StringBuilder();
            sb.Append($"[LogoDiag] load#{load} @{when}: '{GetPath(logo.transform)}' scene='{logo.gameObject.scene.name}' ");
            sb.Append($"activeInHierarchy={logo.gameObject.activeInHierarchy} enabled={logo.enabled} isActiveAndEnabled={logo.isActiveAndEnabled} ");
            sb.Append($"timeScale={Time.timeScale} ");

            if (logo.TryGetComponent(out Graphic g))
            {
                Material m = g.material;
                Material mfr = g.materialForRendering;
                sb.Append($"| graphic.enabled={g.enabled} color.a={g.color.a:F2} crAlpha={g.canvasRenderer.GetAlpha():F2} culled={g.canvasRenderer.cull} ");
                sb.Append($"mat='{(m != null ? m.name : "null")}' burn={(m != null && m.HasProperty(BurnProgressId) ? m.GetFloat(BurnProgressId).ToString("F3") : "n/a")} ");
                sb.Append($"matForRendering='{(mfr != null ? mfr.name : "null")}' burnFR={(mfr != null && mfr.HasProperty(BurnProgressId) ? mfr.GetFloat(BurnProgressId).ToString("F3") : "n/a")} ");
                if (g.canvas != null)
                    sb.Append($"canvas='{g.canvas.name}' canvasEnabled={g.canvas.enabled} ");
            }

            sb.Append("| chain: ");
            for (Transform t = logo.transform; t != null; t = t.parent)
            {
                sb.Append($"{t.name}[self={t.gameObject.activeSelf}");
                if (t.TryGetComponent(out CanvasGroup cg))
                    sb.Append($",cg.a={cg.alpha:F2}{(cg.enabled ? "" : "(off)")}");
                if (t.TryGetComponent(out Animator an))
                    sb.Append($",anim={(an.enabled ? "on" : "off")}");
                sb.Append("] < ");
            }

            Debug.Log(sb.ToString(), logo);
        }
    }

    private static string GetPath(Transform t)
    {
        string path = t.name;
        while ((t = t.parent) != null)
            path = t.name + "/" + path;
        return path;
    }
}
