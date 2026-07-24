using System.Collections;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// A world interactable that opens a face-camera cosmetics view, letting the local player
/// browse and equip hats via <see cref="CosmeticsMenuUI"/>.
///
/// On open:
///   - Smoothly rotates the player's body to face the direction defined by
///     <see cref="_playerFacingTarget"/> (auto-resolved from a child named
///     "Player Facing Target" if not wired in the Inspector).
///   - Calls <see cref="PlayerThirdPersonView.Enter"/> which activates the FaceCamera,
///     restores the head bone scale, shows the body arms, hides FP arms, and disables the
///     FP fill light.
///   - Disables player movement and the interaction reticle.
///   - Shows the Back UI button (Q to exit).
///
/// On close:
///   - Calls <see cref="PlayerThirdPersonView.Exit"/> to revert all visuals.
///   - Re-enables everything in reverse order.
/// </summary>
[RequireComponent(typeof(BoxCollider))]
public class CosmeticsLockerInteractable : Interactable, IHeldItemPassthrough
{
    [Header("Player Facing")]
    [Tooltip("The player's Y rotation is smoothly rotated to match this transform's forward on open. " +
             "Auto-resolved from a child named 'Player Facing Target' if left empty.")]
    [SerializeField] private Transform _playerFacingTarget;

    [Tooltip("Duration in seconds for the body rotation when opening the view.")]
    [SerializeField] private float _rotationDuration = 0.3f;

    [Header("UI")]
    [Tooltip("The cosmetics selection panel to show while this view is open.")]
    [SerializeField] private CosmeticsMenuUI _menuUI;

    [Header("Audio")]
    [Tooltip("AudioSource used to play the open/close SFX. Auto-resolved from this GameObject if left empty.")]
    [SerializeField] private AudioSource _audioSource;

    [Tooltip("Sound played when the locker view opens.")]
    [SerializeField] private AudioClip _openSFX;

    [Tooltip("Sound played when the locker view closes.")]
    [SerializeField] private AudioClip _closeSFX;

    private PlayerInteractionController _interactingPlayer;
    private PlayerThirdPersonView _thirdPersonView;
    private Coroutine _rotationCoroutine;

    protected override void Awake()
    {
        base.Awake();
        interactText = "Cosmetics";

        // Auto-resolve the facing target from a child of this locker.
        if (_playerFacingTarget == null)
            _playerFacingTarget = transform.Find("Player Facing Target");

        // Auto-resolve the AudioSource used for open/close SFX.
        if (_audioSource == null)
            _audioSource = GetComponent<AudioSource>();
    }

    // ─── IInteractable ───────────────────────────────────────────────────────

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (_interactingPlayer != null) return; // Already open.

        _interactingPlayer = player;
        OpenView(player);
    }

    // ─── View lifecycle ──────────────────────────────────────────────────────

    private void OpenView(PlayerInteractionController player)
    {
        // Disable movement and the standard interaction/reticle system.
        player.playerMovementController.SetCanControl(false);
        player.SetSuspectCamMode(true);

        // Smoothly rotate the player body to the facing target's Y angle.
        // NetworkTransform is briefly disabled so it cannot overwrite the rotation
        // each tick — the same pattern used by RagdollController.
        if (_playerFacingTarget != null)
        {
            Vector3 forward = _playerFacingTarget.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude > 0.001f)
            {
                float targetY = Quaternion.LookRotation(forward.normalized).eulerAngles.y;
                _rotationCoroutine = StartCoroutine(RotatePlayerY(player.transform, targetY, _rotationDuration));
            }
        }

        // Snap look angle to straight ahead so the face camera starts centred.
        player.playerMovementController.ResetCameraRotation();

        // Enter third-person view: head bone, body arms, FaceCamera, FP arms, FP light.
        _thirdPersonView = player.GetComponent<PlayerThirdPersonView>();
        _thirdPersonView?.Enter();

        UIController.Instance.ShowCursor();
        UIController.Instance.ShowBackButton(CloseView);
        UIController.Instance.ClosePlayerUI();

        PlaySFX(_openSFX);

        // Open the cosmetics menu UI and bind the local player's hat controller.
        if (_menuUI != null)
        {
            PlayerHatController hatController = player.GetComponent<PlayerHatController>();
            _menuUI.Open(hatController);
        }
    }

    private void CloseView()
    {
        PlaySFX(_closeSFX);

        // Stop any in-progress rotation.
        if (_rotationCoroutine != null)
        {
            StopCoroutine(_rotationCoroutine);
            _rotationCoroutine = null;
        }

        // Close the cosmetics menu UI first so it can clean up bindings.
        _menuUI?.Close();

        // Revert all third-person visuals: FaceCamera, head bone, body arms, FP arms, FP light.
        _thirdPersonView?.Exit();
        _thirdPersonView = null;

        UIController.Instance.HideCursor();
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowPlayerUI();

        // Re-enable player movement and interaction.
        if (_interactingPlayer != null)
        {
            _interactingPlayer.SetSuspectCamMode(false);
            _interactingPlayer.playerMovementController.SetCanControl(true);
            _interactingPlayer = null;
        }
    }

    // ─── Audio ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Plays <paramref name="clip"/> as a one-shot through the locker's own <see cref="_audioSource"/>.
    /// </summary>
    private void PlaySFX(AudioClip clip)
    {
        if (clip == null || _audioSource == null) return;
        _audioSource.PlayOneShot(clip);
    }

    // ─── Rotation coroutine ──────────────────────────────────────────────────

    /// <summary>
    /// Smoothly rotates <paramref name="playerTransform"/> to <paramref name="targetY"/> degrees
    /// on the Y axis over <paramref name="duration"/> seconds using SmoothStep easing.
    /// Temporarily disables the <see cref="NetworkTransform"/> so it cannot overwrite the
    /// rotation each network tick during the tween.
    /// </summary>
    private IEnumerator RotatePlayerY(Transform playerTransform, float targetY, float duration)
    {
        NetworkTransform networkTransform = playerTransform.GetComponent<NetworkTransform>();
        if (networkTransform != null)
            networkTransform.enabled = false;

        float startY  = playerTransform.eulerAngles.y;
        float delta   = Mathf.DeltaAngle(startY, targetY);  // shortest-path signed difference
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            playerTransform.eulerAngles = new Vector3(0f, startY + delta * t, 0f);
            yield return null;
        }

        playerTransform.eulerAngles = new Vector3(0f, targetY, 0f);

        if (networkTransform != null)
            networkTransform.enabled = true;

        _rotationCoroutine = null;
    }
}
