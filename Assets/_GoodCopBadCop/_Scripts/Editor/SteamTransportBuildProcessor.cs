using Netcode.Transports.Facepunch;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Player builds always ship on the Steam transport. While each scene is processed for a build,
/// every NetworkManager's active transport is forced to its FacepunchTransport (enabled), and
/// UnityTransport is disabled. Only the built copy of the scene is changed: the editor scene keeps
/// whatever Tools > Transport Switcher / Quick Connect set, so LAN testing in the editor still works.
/// Fails the build if a NetworkManager has no FacepunchTransport to switch to.
/// </summary>
public class SteamTransportBuildProcessor : IProcessSceneWithReport
{
    public int callbackOrder => 0;

    public void OnProcessScene(Scene scene, BuildReport report)
    {
        // report is null when the scene is processed for Play Mode — leave editor testing alone.
        if (report == null)
            return;

        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var networkManager in root.GetComponentsInChildren<NetworkManager>(true))
                ForceSteamTransport(networkManager, scene);
        }
    }

    private static void ForceSteamTransport(NetworkManager networkManager, Scene scene)
    {
        var facepunch = networkManager.GetComponent<FacepunchTransport>();
        if (facepunch == null)
        {
            throw new BuildFailedException(
                $"[SteamTransportBuild] '{networkManager.name}' in scene '{scene.path}' has no FacepunchTransport. " +
                "Steam networking would not work in this build.");
        }

        var previous = networkManager.NetworkConfig.NetworkTransport;
        networkManager.NetworkConfig.NetworkTransport = facepunch;
        facepunch.enabled = true;

        var unityTransport = networkManager.GetComponent<UnityTransport>();
        if (unityTransport != null)
            unityTransport.enabled = false;

        string previousName = previous != null ? previous.GetType().Name : "none";
        if (previous != facepunch)
            Debug.LogWarning($"[SteamTransportBuild] '{scene.path}': transport was {previousName}, forced to FacepunchTransport for this build.");
        else
            Debug.Log($"[SteamTransportBuild] '{scene.path}': transport is FacepunchTransport.");
    }
}
