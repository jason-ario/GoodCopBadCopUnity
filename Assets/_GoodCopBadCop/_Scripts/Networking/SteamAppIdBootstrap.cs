using Netcode.Transports.Facepunch;
using UnityEngine;

/// <summary>
/// Applies <see cref="GameSettings.SteamAppId"/> (from the build channel) to every
/// <see cref="FacepunchTransport"/> before any scene object's Awake runs, so the App ID
/// serialized on individual transports is ignored.
/// </summary>
public static class SteamAppIdBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Apply()
    {
        var settings = GameSettings.Instance;
        FacepunchTransport.AppIdOverride = settings.SteamAppId;
        Debug.Log($"[SteamAppIdBootstrap] Build channel {settings.Channel} -> Steam AppId {settings.SteamAppId}, version {Application.version}");
    }
}
