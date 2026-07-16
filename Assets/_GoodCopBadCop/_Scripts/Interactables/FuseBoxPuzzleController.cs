using System.Collections;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class FuseBoxPuzzleController : Interactable
{
    [Header("Door")]
    [SerializeField] private Transform _doorHinge;
    [SerializeField] private float _doorOpenAngle = -45f;
    [SerializeField] private float _doorClosedAngle = 90f;
    [SerializeField] private float _doorAnimDuration = 0.4f;

    [Header("Audio")]
    [SerializeField] private AudioSource _audioSource;
    [SerializeField] private AudioClip _doorOpenClip;
    [SerializeField] private AudioClip _doorCloseClip;

    private NetworkVariable<bool> _doorOpen = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private bool _isAnimating = false;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _doorOpen.OnValueChanged += OnDoorStateChanged;

        // Sync visual state on late join
        ApplyDoorInstant(_doorOpen.Value);
    }

    public override void OnNetworkDespawn()
    {
        _doorOpen.OnValueChanged -= OnDoorStateChanged;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (!_isAnimating)
            ToggleDoorServerRpc();
    }

    [ServerRpc(RequireOwnership = false)]
    private void ToggleDoorServerRpc()
    {
        _doorOpen.Value = !_doorOpen.Value;
        BroadcastDoorStateClientRpc(_doorOpen.Value);
    }

    [ClientRpc]
    private void BroadcastDoorStateClientRpc(bool isOpen)
    {
        PlayDoorSound(isOpen);
        StartCoroutine(AnimateDoor(isOpen));
    }

    private void OnDoorStateChanged(bool oldValue, bool newValue)
    {
        // Handled by BroadcastDoorStateClientRpc for connected clients.
        // Only fires for late-joining clients whose state is already synced via OnNetworkSpawn.
    }

    private void PlayDoorSound(bool isOpen)
    {
        if (_audioSource == null) return;
        AudioClip clip = isOpen ? _doorOpenClip : _doorCloseClip;
        if (clip != null)
            _audioSource.PlayOneShot(clip);
    }

    private IEnumerator AnimateDoor(bool isOpen)
    {
        _isAnimating = true;

        float startAngle = _doorHinge.localEulerAngles.y;
        // Normalize start angle into [-180, 180] range so lerp is short-path
        if (startAngle > 180f) startAngle -= 360f;

        float targetAngle = isOpen ? _doorOpenAngle : _doorClosedAngle;
        float elapsed = 0f;

        while (elapsed < _doorAnimDuration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, elapsed / _doorAnimDuration);
            float angle = Mathf.Lerp(startAngle, targetAngle, t);
            _doorHinge.localEulerAngles = new Vector3(
                _doorHinge.localEulerAngles.x,
                angle,
                _doorHinge.localEulerAngles.z
            );
            yield return null;
        }

        _doorHinge.localEulerAngles = new Vector3(
            _doorHinge.localEulerAngles.x,
            targetAngle,
            _doorHinge.localEulerAngles.z
        );

        _isAnimating = false;
    }

    private void ApplyDoorInstant(bool isOpen)
    {
        if (_doorHinge == null) return;
        float angle = isOpen ? _doorOpenAngle : _doorClosedAngle;
        _doorHinge.localEulerAngles = new Vector3(
            _doorHinge.localEulerAngles.x,
            angle,
            _doorHinge.localEulerAngles.z
        );
    }
}
