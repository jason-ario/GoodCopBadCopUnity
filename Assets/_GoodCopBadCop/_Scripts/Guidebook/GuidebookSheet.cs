using UnityEngine;

/// <summary>
/// One physical, double-sided guidebook sheet. Pooled and filled by <see cref="GuidebookBuilder"/>,
/// flipped by <see cref="GuidebookPageController"/>.
///
/// The front face is visible while the sheet sits on the right (unread) stack; the back face
/// is visible once it has been flipped onto the left (read) stack.
/// </summary>
public class GuidebookSheet : MonoBehaviour
{
    [SerializeField] private GuidebookFaceView _front;
    [SerializeField] private GuidebookFaceView _back;

    [Tooltip("Parent for section tabs attached to this sheet. Defaults to the sheet root.")]
    [SerializeField] private Transform _tabAnchor;

    public GuidebookFaceView Front => _front;
    public GuidebookFaceView Back  => _back;
    public Transform TabAnchor => _tabAnchor != null ? _tabAnchor : transform;

    /// <summary>
    /// Shows at most one face. Pass <paramref name="visible"/> = false to hide both faces
    /// (sheets buried in a stack never need their canvases rendered).
    /// </summary>
    public void SetFace(bool showBack, bool visible = true)
    {
        if (_front != null) _front.gameObject.SetActive(visible && !showBack);
        if (_back  != null) _back.gameObject.SetActive(visible && showBack);
    }
}
