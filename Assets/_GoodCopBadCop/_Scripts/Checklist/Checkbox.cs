using System.Collections;
using UnityEngine;

public class Checkbox : MonoBehaviour, IClickable
{
    [SerializeField] private GameObject checkmark;
    [SerializeField] private ChecklistItem checklistItem;
    [SerializeField] private SpriteRenderer spriteRenderer;
    [SerializeField] private Animator ikAnimationTarget;
    [SerializeField] private Transform ikTargetTransform;
    [SerializeField] private AudioClip drawSound;
    [Tooltip("Volume multiplier for the draw sound. Values above 1 boost the clip (it is quiet at 1).")]
    [SerializeField] [Range(0f, 8f)] private float drawVolume = 5f;

    /// <summary>
    /// Visual indicator (the "Selected Box" child) shown while this checkbox is the
    /// currently highlighted item during controller navigation. Hidden by default.
    /// </summary>
    [SerializeField] private GameObject selectedBox;

    private bool _isInteractable = false;
    public bool IsChecked { get; private set; }

    /// <summary>True while this checkbox can be clicked/toggled (exam is active and unlocked).</summary>
    public bool IsInteractable => _isInteractable;

    private void OnEnable()
    {
        spriteRenderer.color = Color.clear;
    }

    /// <summary>
    /// Shows the checkmark sprite. Called on all clients via ExamNotebook's NetworkVariable callback.
    /// Does NOT trigger the IK arm animation — that is local-only and fires in OnClick instead.
    /// </summary>
    /// <param name="playSound">
    /// False for state that is being restored/synced rather than ticked by a player (save resume,
    /// spawn seeding), so loading a notebook with checked boxes doesn't play the pen sound.
    /// </param>
    public void CheckVisual(bool playSound = true)
    {
        IsChecked = true;
        ikAnimationTarget.SetTrigger("Check");
        StartCoroutine(WaitAndShowCheckmark(playSound));
    }

    private IEnumerator WaitAndShowCheckmark(bool playSound)
    {
        yield return new WaitForSeconds(.15f);
        checkmark.SetActive(true);
        if (playSound)
            SFXController.Instance.Play(drawSound, drawVolume);
    }

    /// <summary>Hides the checkmark sprite and clears the checked state.</summary>
    public void Uncheck()
    {
        IsChecked = false;
        checkmark.SetActive(false);
    }

    /// <summary>
    /// Handles a local click. Triggers the IK arm animation immediately for the local player,
    /// then routes the state change through the server so all clients stay in sync.
    /// </summary>
    public void OnClick()
    {
        if (!_isInteractable) return;

        bool newValue = !IsChecked;

        // Trigger the arm IK animation locally for the player doing the clicking.
        // This must NOT go through the network callback because AnimateCheckMark accesses
        // playerPickupController, which is only valid on the player holding the notebook.
        if (newValue)
            checklistItem.AnimateCheckMark(ikTargetTransform);

        checklistItem.OnCheckboxClicked(IsChecked);
    }

    public void SetInteractable(bool value)
    {
        _isInteractable = value;
    }

    /// <summary>
    /// Shows or hides the "Selected Box" highlight used to indicate which checkbox is
    /// currently focused during controller (gamepad) navigation.
    /// </summary>
    public void SetSelected(bool value)
    {
        if (selectedBox != null)
            selectedBox.SetActive(value);
    }
}
