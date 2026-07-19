using System.Collections;
using System.IO;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Periodically captures a square (1:1) thumbnail of the active gameplay session
/// and writes it to disk so the campaign screen can display it as a save-slot thumbnail.
///
/// Place this on a persistent GameObject in the gameplay scene.
/// Screenshots are stored at <see cref="GetScreenshotPath(int)"/> and
/// are named <c>screenshot_slot_{index}.png</c> in <see cref="Application.persistentDataPath"/>.
///
/// Capture starts only after <see cref="GameManager.OnGameStart"/> fires so the player
/// is guaranteed to be in-world. The UI layer is excluded via culling mask — no canvas
/// toggling or full-screen readback; the camera renders directly to a small RenderTexture.
/// </summary>
public class SaveScreenshotManager : MonoBehaviour
{
    public static SaveScreenshotManager Instance { get; private set; }

    [Header("Settings")]
    [Tooltip("Seconds between automatic screenshots during gameplay. Default is 180 (3 minutes).")]
    [SerializeField] private float intervalSeconds = 180f;

    [Tooltip("Width and height of the saved thumbnail in pixels (square).")]
    [SerializeField] private int thumbnailSize = 256;

    private const string FileNameFormat = "screenshot_slot_{0}.png";

    private Coroutine _intervalCoroutine;
    private bool _gameStarted;

    // ---------------------------------------------------------------------------
    // Unity Lifecycle
    // ---------------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        Debug.Log($"[SaveScreenshotManager] Start — persistentDataPath: {Application.persistentDataPath}");
        StartCoroutine(WaitForGameStartThenBegin());
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;

        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStart -= OnGameStart;
    }

    // ---------------------------------------------------------------------------
    // Public API
    // ---------------------------------------------------------------------------

    /// <summary>
    /// Returns the absolute on-disk path for the screenshot belonging to <paramref name="slotIndex"/>.
    /// </summary>
    public static string GetScreenshotPath(int slotIndex)
    {
        return Path.Combine(Application.persistentDataPath, string.Format(FileNameFormat, slotIndex));
    }

    /// <summary>
    /// Deletes the screenshot file for the given slot index if it exists.
    /// Called by <see cref="SaveDataManager"/> when a slot is wiped.
    /// </summary>
    public static void DeleteScreenshot(int slotIndex)
    {
        string path = GetScreenshotPath(slotIndex);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveScreenshotManager] Screenshot deleted for slot {slotIndex}.");
        }
    }

    /// <summary>Immediately triggers a screenshot capture for the current active slot.</summary>
    public void TakeScreenshot()
    {
        if (!_gameStarted || !CanCapture()) return;
        StartCoroutine(CaptureRoutine());
    }

    // ---------------------------------------------------------------------------
    // Startup sequence
    // ---------------------------------------------------------------------------

    private IEnumerator WaitForGameStartThenBegin()
    {
        // Step 1 — wait for SaveDataManager to have an active slot.
        int waited = 0;
        while (SaveDataManager.Instance == null || SaveDataManager.Instance.ActiveSlotIndex < 0)
        {
            waited++;
            if (waited % 60 == 0)
                Debug.Log($"[SaveScreenshotManager] Waiting for active slot... " +
                          $"(frames: {waited}, ActiveSlotIndex={SaveDataManager.Instance?.ActiveSlotIndex ?? -99})");
            yield return null;
        }

        Debug.Log($"[SaveScreenshotManager] Active slot ready: {SaveDataManager.Instance.ActiveSlotIndex}. " +
                  "Subscribing to GameManager.OnGameStart...");

        // Step 2 — wait for GameManager, then subscribe to OnGameStart.
        while (GameManager.Instance == null)
            yield return null;

        GameManager.Instance.OnGameStart += OnGameStart;

        // Step 3 — wait for the event (player is fully in-world).
        yield return new WaitUntil(() => _gameStarted);

        Debug.Log("[SaveScreenshotManager] GameManager.OnGameStart received — player is in-world. Starting capture.");

        if (!IsNetworkAllowed())
        {
            Debug.Log("[SaveScreenshotManager] Client peer — screenshots are host/server only. Stopping.");
            yield break;
        }

        StartCoroutine(CaptureRoutine());
        _intervalCoroutine = StartCoroutine(IntervalRoutine());
    }

    private void OnGameStart()
    {
        _gameStarted = true;
        if (GameManager.Instance != null)
            GameManager.Instance.OnGameStart -= OnGameStart;
    }

    // ---------------------------------------------------------------------------
    // Capture helpers
    // ---------------------------------------------------------------------------

    private bool CanCapture()
    {
        if (SaveDataManager.Instance == null || SaveDataManager.Instance.ActiveSlotIndex < 0)
            return false;
        return IsNetworkAllowed();
    }

    private static bool IsNetworkAllowed()
    {
        var nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening) return true;
        return nm.IsHost || nm.IsServer;
    }

    private IEnumerator IntervalRoutine()
    {
        while (true)
        {
            yield return new WaitForSeconds(intervalSeconds);
            if (CanCapture())
                yield return CaptureRoutine();
        }
    }

    private IEnumerator CaptureRoutine()
    {
        // Yield one frame so all game-state updates (animations, positions) are settled.
        yield return null;

        int slotIndex = SaveDataManager.Instance != null ? SaveDataManager.Instance.ActiveSlotIndex : -1;
        if (slotIndex < 0)
        {
            Debug.LogWarning("[SaveScreenshotManager] CaptureRoutine — no active slot, aborting.");
            yield break;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            Debug.LogWarning("[SaveScreenshotManager] CaptureRoutine — Camera.main is null, aborting.");
            yield break;
        }

        Debug.Log($"[SaveScreenshotManager] CaptureRoutine — rendering {thumbnailSize}x{thumbnailSize} for slot {slotIndex}.");

        // Render the camera directly to a small RenderTexture — no full-screen readback needed.
        RenderTexture rt = new RenderTexture(thumbnailSize, thumbnailSize, 24, RenderTextureFormat.Default);

        RenderTexture originalTarget = cam.targetTexture;
        int originalMask = cam.cullingMask;

        // Exclude the UI layer so canvases don't appear in the thumbnail.
        cam.cullingMask = originalMask & ~(1 << LayerMask.NameToLayer("UI"));
        cam.targetTexture = rt;
        cam.Render();

        // Restore the camera immediately.
        cam.targetTexture = originalTarget;
        cam.cullingMask = originalMask;

        // Read pixels from the RT and encode to PNG.
        Texture2D thumbnail = new Texture2D(thumbnailSize, thumbnailSize, TextureFormat.RGB24, false);
        RenderTexture.active = rt;
        thumbnail.ReadPixels(new Rect(0, 0, thumbnailSize, thumbnailSize), 0, 0);
        thumbnail.Apply();
        RenderTexture.active = null;
        rt.Release();
        Destroy(rt);

        byte[] png = thumbnail.EncodeToPNG();
        Destroy(thumbnail);

        string path = GetScreenshotPath(slotIndex);
        File.WriteAllBytes(path, png);
        Debug.Log($"[SaveScreenshotManager] Screenshot saved ({png.Length} bytes) for slot {slotIndex} at: {path}");
    }
}
