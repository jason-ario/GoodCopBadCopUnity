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

        if (IsOwner)
        {
            // Hide the local player's own name tag.
            gameObject.SetActive(false);

            // Only set the Steam name when using Facepunch transport.
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.NetworkConfig.NetworkTransport is FacepunchTransport)
            {
                string steamName = SteamClient.Name;
                if (string.IsNullOrEmpty(steamName))
                    Debug.LogWarning("[NameTag] SteamClient.Name is empty at spawn time — name tag will be blank for other players.");

                _playerName.Value = new FixedString64Bytes(steamName ?? string.Empty);
            }

            // No need to observe our own variable changes.
            return;
        }

        // Subscribe before reading the current value so we never miss a change.
        _playerName.OnValueChanged += OnPlayerNameChanged;

        // Apply whatever value is already replicated; OnValueChanged covers future updates.
        string currentName = _playerName.Value.ToString();
        if (!string.IsNullOrEmpty(currentName))
            ApplyName(currentName);
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();

        if (!IsOwner)
            _playerName.OnValueChanged -= OnPlayerNameChanged;
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
