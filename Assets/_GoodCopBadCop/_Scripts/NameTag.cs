using System.Collections;
using Netcode.Transports.Facepunch;
using Steamworks;
using Steamworks.Data;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class NameTag : NetworkBehaviour
{
    [Tooltip("The transform to follow (e.g. head bone of the character)")]
    public Transform target;

    [Tooltip("Optional offset from the target position (e.g. to float above the head)")]
    public Vector3 offset = new Vector3(0f, 0.2f, 0f);

    [SerializeField] private TMP_Text label;

    /// <summary>
    /// Stores the owner's Steam ID so every client can independently resolve
    /// the display name — avoiding any replication-timing race on FixedString.
    /// Written by the owner; readable by all.
    /// </summary>
    private readonly NetworkVariable<ulong> _ownerSteamId =
        new NetworkVariable<ulong>(
            0UL,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    private Camera _camera;

    private void OnEnable()
    {
        _camera = Camera.main;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _ownerSteamId.OnValueChanged += OnSteamIdChanged;

        if (IsOwner)
        {
            // Publish the local player's Steam ID so every other client can resolve the name.
            StartCoroutine(PublishSteamIdNextFrame());
            return;
        }

        // Non-owner: if the NetworkVariable snapshot already contains a valid ID
        // (e.g. late-joiner receiving initial state), resolve immediately.
        if (_ownerSteamId.Value != 0UL)
            StartCoroutine(ResolveSteamName(_ownerSteamId.Value));
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _ownerSteamId.OnValueChanged -= OnSteamIdChanged;
    }

    /// <summary>Waits one frame then writes the local SteamId into the NetworkVariable.</summary>
    private IEnumerator PublishSteamIdNextFrame()
    {
        yield return null;

        bool usesSteam = NetworkManager.Singleton != null &&
                         NetworkManager.Singleton.NetworkConfig.NetworkTransport is FacepunchTransport;

        if (!usesSteam || !SteamClient.IsValid)
            yield break;

        _ownerSteamId.Value = SteamClient.SteamId.Value;
    }

    private void OnSteamIdChanged(ulong previous, ulong current)
    {
        // Only non-owners need to react; owners never show their own tag.
        if (IsOwner || current == 0UL)
            return;

        StartCoroutine(ResolveSteamName(current));
    }

    /// <summary>
    /// Resolves the display name for <paramref name="steamId"/> by calling
    /// <see cref="Friend.RequestInfoAsync"/> so the name is fetched from Steam
    /// even when the user is not in the local friend list.
    /// </summary>
    private IEnumerator ResolveSteamName(ulong steamId)
    {
        if (!SteamClient.IsValid)
            yield break;

        var friend = new Friend(steamId);
        var task = friend.RequestInfoAsync();

        // Yield until the async request completes.
        while (!task.IsCompleted)
            yield return null;

        string resolvedName = friend.Name;

        if (string.IsNullOrEmpty(resolvedName))
        {
            Debug.LogWarning($"[NameTag] Could not resolve Steam name for SteamId={steamId}.");
            yield break;
        }

        ApplyName(resolvedName);
    }

    private void ApplyName(string playerName)
    {
        if (label != null)
            label.text = playerName;
    }

    private void LateUpdate()
    {
        if (target == null || _camera == null) return;

        // Follow the target's position with an optional offset.
        transform.position = target.position + offset;

        // Face the camera: copy the camera's rotation so the tag always looks at the viewer.
        transform.rotation = _camera.transform.rotation;
    }
}
