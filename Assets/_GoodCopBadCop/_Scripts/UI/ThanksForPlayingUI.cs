using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the "Thanks for Playing the Demo" end screen shown when the final campaign day completes.
/// Provides buttons to wishlist on Steam and return to the main menu.
/// Opened via <see cref="UIController.ShowThanksForPlayingScreen"/>; closed when the player leaves the session.
/// Voice chat keeps running while this screen is up (see <see cref="AudioManager.SilenceWorldAudio"/>).
/// </summary>
public class ThanksForPlayingUI : MonoBehaviour
{
    [Tooltip("Steam store page URL to open when the player clicks Wishlist on Steam.")]
    [SerializeField] private string _steamWishlistUrl = "https://store.steampowered.com/app/APPID/";

    private bool _isReturningToMenu;

    // ─── Lifecycle ───────────────────────────────────────────────────────────

    private void OnDisable()
    {
        // The end screen pauses world audio via AudioListener.pause (global) — always undo it
        // when this screen goes away, however it was closed.
        AudioManager.Instance?.RestoreWorldAudio();
    }

    // ─── Button Handlers ─────────────────────────────────────────────────────

    /// <summary>Called by the Wishlist on Steam button's OnClick event.</summary>
    public void OnWishlistOnSteamClicked()
    {
        Application.OpenURL(_steamWishlistUrl);
    }

    /// <summary>
    /// Called by the Return to Main Menu button's OnClick event.
    /// Shuts down the network session and reloads the scene, which boots back into the main menu
    /// (same flow as the death screen / pause menu).
    /// </summary>
    public async void OnReturnToMainMenuClicked()
    {
        if (_isReturningToMenu)
            return;

        _isReturningToMenu = true;

        try
        {
            if (LobbyManager.Instance != null)
                await LobbyManager.Instance.ExitLobbyAsync();
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
        }

        // AudioListener.pause is global and survives scene loads — release it before reloading.
        AudioManager.Instance?.RestoreWorldAudio();
        SceneManager.LoadScene(SceneManager.GetActiveScene().path);
    }
}
