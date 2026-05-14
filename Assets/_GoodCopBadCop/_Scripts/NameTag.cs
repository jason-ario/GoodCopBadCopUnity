using System.Collections;
using Netcode.Transports.Facepunch;
using Steamworks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

public class NameTag : NetworkBehaviour
{
    [Tooltip("The transform to follow (e.g. head bone of the character)")]
    public Transform target;

    [Tooltip("Optional offset from the target position (e.g. to float above the head)")]
    public Vector3 offset = new Vector3(0f, 0.2f, 0f);

    [SerializeField] private TMP_Text label;

    private readonly NetworkVariable<FixedString64Bytes> _playerName =
        new NetworkVariable<FixedString64Bytes>(
            default,
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

        // Always subscribe on all clients — the owner's own tag is hidden anyway,
        // but subscribing unconditionally avoids missing an update if ownership transfers.
        _playerName.OnValueChanged += OnPlayerNameChanged;

        if (IsOwner)
        {
            // Defer the write by one frame so NGO has finished initialising the
            // NetworkVariable on all connected clients before the value replicates.
            StartCoroutine(SetNameNextFrame());
            return;
        }

        // Non-owner: apply whatever value is already present (covers late-joiners
        // whose NetworkVariable snapshot already contains the correct name).
        string currentName = _playerName.Value.ToString();
        if (!string.IsNullOrEmpty(currentName))
            ApplyName(currentName);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
        _playerName.OnValueChanged -= OnPlayerNameChanged;
    }

    /// <summary>Waits one frame then writes the local Steam display name into the NetworkVariable.</summary>
    private IEnumerator SetNameNextFrame()
    {
        yield return null;

        bool usesSteam = NetworkManager.Singleton != null &&
                         NetworkManager.Singleton.NetworkConfig.NetworkTransport is FacepunchTransport;

        if (!usesSteam)
            yield break;

        string steamName = SteamClient.Name;
        if (string.IsNullOrEmpty(steamName))
        {
            Debug.LogWarning("[NameTag] SteamClient.Name is empty after one frame — name tag will be blank for other players.");
            yield break;
        }

        _playerName.Value = new FixedString64Bytes(steamName);
    }

    private void OnPlayerNameChanged(FixedString64Bytes previous, FixedString64Bytes current)
    {
        ApplyName(current.ToString());
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
