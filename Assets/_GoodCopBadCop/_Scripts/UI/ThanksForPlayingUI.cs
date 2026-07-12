using UnityEngine;

/// <summary>
/// Drives the "Thanks for Playing the Demo" end screen shown when the final campaign day completes.
/// Provides buttons to wishlist on Steam and return to the main menu.
/// Opened via <see cref="UIController.ShowThanksForPlayingScreen"/>; closed when the player leaves the session.
/// </summary>
public class ThanksForPlayingUI : MonoBehaviour
{
    [Tooltip("Steam store page URL to open when the player clicks Wishlist on Steam.")]
    [SerializeField] private string _steamWishlistUrl = "https://store.steampowered.com/app/APPID/";

    // ─── Button Handlers ─────────────────────────────────────────────────────

    /// <summary>Called by the Wishlist on Steam button's OnClick event.</summary>
    public void OnWishlistOnSteamClicked()
    {
        Application.OpenURL(_steamWishlistUrl);
    }

    /// <summary>
    /// Called by the Return to Main Menu button's OnClick event.
    /// Shuts down the network session and returns the player to the home screen.
    /// </summary>
    public async void OnReturnToMainMenuClicked()
    {
        if (LobbyManager.Instance != null)
            await LobbyManager.Instance.ExitLobbyAsync();

        MainMenuController.Instance.BackToHomeScreen();
    }
}
