using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using TMPro;

/// <summary>
/// Displays today's prohibited goods categories on the "Prohibited Goods Sign" — reads directly
/// from <see cref="SortMailTask.ProhibitedGoodsToday"/>, which is replicated to all clients, so
/// the sign always matches whatever <see cref="SortMailTask"/> chose for the current delivery.
///
/// Plain MonoBehaviour (not networked) — it just observes the already-replicated
/// <see cref="NetworkList{T}"/> on <see cref="SortMailTask"/>. Since SortMailTask is a scene
/// object, it may not be spawned yet when this component enables, so binding is retried until
/// it becomes available (covers both host/server start-up and late-joining clients).
///
/// Scene setup: assign _goodsTypeTexts to the "Prohibited Good Type 1/2/3" TextMeshProUGUI
/// children under Canvas/GameObject, in display order.
/// </summary>
public class ProhibitedGoodsSignDisplay : MonoBehaviour
{
    [Tooltip("TMP labels on the sign, one per prohibited-goods slot, in display order.")]
    [SerializeField] private TextMeshProUGUI[] _goodsTypeTexts;

    [Tooltip("Text shown in a slot when there is no prohibited category for it today.")]
    [SerializeField] private string _emptySlotText = "";

    private SortMailTask _boundTask;

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
    }
}
