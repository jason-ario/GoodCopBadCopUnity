using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays today's prohibited goods categories on the "Prohibited Goods Sign" — reads directly
/// from <see cref="SortMailTask.ProhibitedGoodsToday"/>, which is replicated to all clients, so
/// the sign always matches whatever <see cref="SortMailTask"/> chose for the current delivery.
/// <see cref="SortMailTask"/> rolls today's categories at day start (see
/// <see cref="SortMailTask.OnDayChanged"/>), so this display refreshes at day start too, not
/// whenever the mail task itself actually kicks off.
///
/// Plain MonoBehaviour (not networked) — it just observes the already-replicated
/// <see cref="NetworkList{T}"/> on <see cref="SortMailTask"/>. Since SortMailTask is a scene
/// object, it may not be spawned yet when this component enables, so binding is retried until
/// it becomes available (covers both host/server start-up and late-joining clients).
///
/// Render-texture overlay: the text lives on the "HiddenUI" layer inside the WorldSpace Canvas,
/// which <see cref="_renderCamera"/> captures into the sign mesh's material (via its
/// <c>_OverlayMap</c> slot). The Canvas itself is never rendered directly by gameplay cameras —
/// only the resulting texture is visible, composited onto the sign's paper/base texture. This
/// mirrors the RenderTexture approach used by <see cref="ExamPage"/> and
/// <see cref="NewspaperContentsController"/>.
///
/// Scene setup: assign _goodsTypeTexts to the "Prohibited Good Type 1/2/3" TextMeshProUGUI
/// children under Canvas/GameObject, in display order. Assign _renderCamera to the "Sign Render
/// Camera" child (kept inactive except while snapshotting).
/// </summary>
public class ProhibitedGoodsSignDisplay : MonoBehaviour
{
    [Tooltip("TMP labels on the sign, one per prohibited-goods slot, in display order.")]
    [SerializeField] private TextMeshProUGUI[] _goodsTypeTexts;

    [Tooltip("Text shown in a slot when there is no prohibited category for it today.")]
    [SerializeField] private string _emptySlotText = "";

    [Tooltip("Camera that renders the hidden WorldSpace canvas into the sign material's overlay " +
             "RenderTexture. Kept inactive except while snapshotting.")]
    [SerializeField] private GameObject _renderCamera;

    [Tooltip("The WorldSpace canvas holding the sign's HiddenUI text. Kept inactive except while " +
             "the render camera is capturing it, so it never sits around doing nothing.")]
    [SerializeField] private GameObject _signCanvas;

    private SortMailTask _boundTask;
    private Coroutine _snapshotCoroutine;

    private void OnEnable()
    {
        TryBind();
    }

    private void OnDisable()
    {
        Unbind();
    }

    private void Update()
    {
        if (_boundTask == null)
            TryBind();
    }

    private void TryBind()
    {
        SortMailTask task = SortMailTask.Instance;
        if (task == null || !task.IsSpawned) return;

        _boundTask = task;
        _boundTask.ProhibitedGoodsToday.OnListChanged += HandleProhibitedGoodsChanged;
        RefreshDisplay();
    }

    private void Unbind()
    {
        if (_boundTask == null) return;
        _boundTask.ProhibitedGoodsToday.OnListChanged -= HandleProhibitedGoodsChanged;
        _boundTask = null;
    }

    private void HandleProhibitedGoodsChanged(NetworkListEvent<FixedString64Bytes> _)
    {
        RefreshDisplay();
    }

    private void RefreshDisplay()
    {
        if (_boundTask == null || _goodsTypeTexts == null) return;

        NetworkList<FixedString64Bytes> prohibited = _boundTask.ProhibitedGoodsToday;
        for (int i = 0; i < _goodsTypeTexts.Length; i++)
        {
            if (_goodsTypeTexts[i] == null) continue;
            _goodsTypeTexts[i].text = i < prohibited.Count ? prohibited[i].ToString() : _emptySlotText;
        }

        RequestSnapshot();
    }

    /// <summary>
    /// Re-renders the sign's overlay RenderTexture from the current canvas contents. Restarts if
    /// called again while already running (e.g. a rapid double-update of the goods list).
    /// </summary>
    private void RequestSnapshot()
    {
        if (_renderCamera == null) return;

        if (_snapshotCoroutine != null)
            StopCoroutine(_snapshotCoroutine);
        _snapshotCoroutine = StartCoroutine(SnapshotRoutine());
    }

    private IEnumerator SnapshotRoutine()
    {
        // Bring the canvas back to life before the camera captures it — it deactivates again
        // below once the render is done, so it never sits around active without being rendered.
        if (_signCanvas != null)
            _signCanvas.SetActive(true);

        // Let the TMP text changes above finish their mesh rebuild before the camera captures a
        // frame, then hold the camera active for one more frame so the render actually lands in
        // the RenderTexture before switching it back off.
        yield return new WaitForEndOfFrame();
        _renderCamera.SetActive(true);
        yield return new WaitForEndOfFrame();
        _renderCamera.SetActive(false);

        if (_signCanvas != null)
            _signCanvas.SetActive(false);

        _snapshotCoroutine = null;
    }
}

