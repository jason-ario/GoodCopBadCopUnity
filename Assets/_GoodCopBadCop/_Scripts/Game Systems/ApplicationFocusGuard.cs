using System.Collections;
using UnityEngine;

/// <summary>
/// Recovers input after the game window loses focus (OS/app notifications, alt-tab, overlays).
/// The OS releases the cursor lock on focus loss and Unity does not re-apply it on focus regain,
/// and the legacy Input Manager misses key-up events while unfocused. Without this the player
/// returns to a "frozen" game: look/move stop responding and held keys stay stuck.
/// Bootstraps itself; no scene setup needed.
/// </summary>
public class ApplicationFocusGuard : MonoBehaviour
{
    private static ApplicationFocusGuard s_instance;

    [Tooltip("Open the pause menu when focus is lost during active gameplay (player builds only).")]
    [SerializeField] private bool pauseOnFocusLost = true;

    private Coroutine _restoreRoutine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (s_instance != null) return;
        var go = new GameObject(nameof(ApplicationFocusGuard));
        DontDestroyOnLoad(go);
        s_instance = go.AddComponent<ApplicationFocusGuard>();
    }

    private void OnDestroy()
    {
        if (s_instance == this) s_instance = null;
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            if (_restoreRoutine != null) StopCoroutine(_restoreRoutine);
            _restoreRoutine = StartCoroutine(RestoreAfterFocus());
        }
        else
        {
            TryAutoPause();
        }
    }

    private void OnApplicationPause(bool paused)
    {
        // Some platforms report backgrounding through pause instead of focus.
        if (!paused) OnApplicationFocus(true);
    }

    private void TryAutoPause()
    {
        // Clicking out of the Game View in the Editor shouldn't pause every time.
        if (!pauseOnFocusLost || Application.isEditor) return;

        UIController ui = UIController.Instance;
        PlayerInstance player = PlayerInstance.Instance;
        if (ui == null || player == null || ui.IsPaused || UIController.IsPauseBlocked) return;

        // Only during free gameplay (cursor locked, player in control). Dialogue, terminals and
        // other UI states keep their own cursor ownership and are restored in RestoreAfterFocus.
        var movement = player.GetComponent<PlayerMovementController>();
        if (!player.CanControl || movement == null || !movement.CanLook) return;

        ui.OpenPauseMenu();
    }

    private IEnumerator RestoreAfterFocus()
    {
        // Clear axes/buttons whose key-up was lost while unfocused (stuck movement/mouse delta).
        Input.ResetInputAxes();

        // Let the OS finish handing focus back before re-applying the cursor.
        yield return null;

        // Unity still reports the state gameplay last requested, but the OS dropped it.
        // Re-applying the same value is a no-op, so toggle through None to force it.
        CursorLockMode desiredLock = Cursor.lockState;
        bool desiredVisible = Cursor.visible;
        if (desiredLock != CursorLockMode.None)
        {
            Cursor.lockState = CursorLockMode.None;
            yield return null;
            // Skip if another system set its own cursor state during that frame.
            if (Cursor.lockState == CursorLockMode.None)
            {
                Cursor.lockState = desiredLock;
                Cursor.visible = desiredVisible;
            }
        }
        else
        {
            Cursor.visible = desiredVisible;
        }

        Input.ResetInputAxes();
        _restoreRoutine = null;
    }
}
