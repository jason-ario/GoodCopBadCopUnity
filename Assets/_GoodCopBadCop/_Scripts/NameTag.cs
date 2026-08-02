using Steamworks;
using TMPro;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Place on the player root. Assign the separate name tag GameObject in the inspector.
/// </summary>
public class NameTag : NetworkBehaviour
{
    [Tooltip("The name tag GameObject to show above this player (separate from the player root)")]
    [SerializeField] private GameObject nameTagObject;

    [Tooltip("The TMP label on the name tag object")]
    [SerializeField] private TMP_Text label;

    [Tooltip("Transform to position the name tag at (e.g. head bone)")]
    [SerializeField] private Transform followTarget;

    [Tooltip("Offset above the follow target")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.2f, 0f);

    /// <summary>
    /// The owner writes their Steam display name here; all other clients read it.
    /// </summary>
    private readonly NetworkVariable<FixedString64Bytes> _ownerName =
        new NetworkVariable<FixedString64Bytes>(
            default,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Owner);

    private Camera _camera;
    private bool _nameResolved;

    private void OnEnable()
    {
        _camera = Camera.main;
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        if (IsOwner)
        {
            // Owner reads their own Steam name directly and publishes it for all other clients.
            if (!SteamClient.IsValid)
            {
                Debug.LogWarning("[NameTag] Steam is not initialized.");
                return;
            }

            string steamName = SteamClient.Name;
            Debug.Log($"[NameTag] Publishing Steam name: {steamName}");
            _ownerName.Value = new FixedString64Bytes(steamName);

            // Ensure the local player's own name tag stays hidden.
            if (nameTagObject != null)
                nameTagObject.SetActive(false);

            return;
        }

        // Non-owner: show the name tag and apply immediately if the name snapshot already arrived (e.g. late joiner).
        if (nameTagObject != null)
            nameTagObject.SetActive(true);

        string existingName = _ownerName.Value.ToString();
        if (!string.IsNullOrEmpty(existingName))
        {
            ApplyName(existingName);
            _nameResolved = true;
        }
    }

    /// <summary>Polls each frame until the owner's name arrives over the network.</summary>
    private void Update()
    {
        if (IsOwner || _nameResolved) return;

        string name = _ownerName.Value.ToString();
        if (!string.IsNullOrEmpty(name))
        {
            ApplyName(name);
            _nameResolved = true;
        }
    }

    private void ApplyName(string playerName)
    {
        if (label != null)
            label.text = playerName;
    }

    private void LateUpdate()
    {
        if (IsOwner || nameTagObject == null || followTarget == null || _camera == null) return;

        // Position and billboard the name tag object each frame, facing the camera on the
        // Y axis only so it stays upright instead of tilting with the camera's pitch/roll.
        nameTagObject.transform.position = followTarget.position + offset;

        Vector3 directionToCamera = nameTagObject.transform.position - _camera.transform.position;
        directionToCamera.y = 0f;

        if (directionToCamera.sqrMagnitude > 0.0001f)
            nameTagObject.transform.rotation = Quaternion.LookRotation(directionToCamera, Vector3.up);
    }
}
