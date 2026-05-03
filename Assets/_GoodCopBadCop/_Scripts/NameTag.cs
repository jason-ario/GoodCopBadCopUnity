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

        _playerName.OnValueChanged += OnPlayerNameChanged;

        if (IsOwner)
        {
            // Hide the local player's own name tag.
            gameObject.SetActive(false);

            // Only set the Steam name when using Facepunch transport.
            if (NetworkManager.Singleton != null &&
                NetworkManager.Singleton.NetworkConfig.NetworkTransport is FacepunchTransport)
            {
                _playerName.Value = new FixedString64Bytes(SteamClient.Name);
            }
        }
        else
        {
            // Apply the current value if it has already been synced by the time we spawn.
            // OnValueChanged will handle late arrivals if the value isn't set yet.
            string currentName = _playerName.Value.ToString();
            if (!string.IsNullOrEmpty(currentName))
                ApplyName(currentName);
        }
    }

    public override void OnNetworkDespawn()
    {
        base.OnNetworkDespawn();
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
