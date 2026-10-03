using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Example state shown by the guidebook's printed copy of the HUD Checkpoint Integrity panel.</summary>
[Serializable]
public class GuidebookIntegrityPanel
{
    [Range(0, 100)] public int Percent = 100;
    [Min(0)] public int FenceCount;
    [Min(0)] public int TrashCount;
    [Min(0)] public int GraffitiCount;
}

/// <summary>
/// Prints the real HUD Checkpoint Integrity panel onto a guidebook face. The first build copies the
/// scene's <see cref="CheckpointIntegrityBar"/> panel (frame, fonts, squares, counters) via
/// <see cref="GuidebookHudCopy"/>, which strips its live-data behaviours and makes it lit like the page.
/// The copy's own <see cref="CheckpointIntegrityBar"/> renders the example score with the HUD's colors
/// via <see cref="CheckpointIntegrityBar.ShowPreview"/>. Created at runtime by <see cref="GuidebookFaceView"/>.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GuidebookIntegrityPanelView : MonoBehaviour
{
    private TMP_Text _style;
    private Material _imageMaterial;

    private RectTransform          _copy;
    private CheckpointIntegrityBar _bar;
    private TMP_Text _fenceCount, _trashCount, _graffitiCount;
    private bool _searched;

    private RectTransform Rect => (RectTransform)transform;

    public void Configure(TMP_Text style, Material imageMaterial)
    {
        _style         = style;
        _imageMaterial = imageMaterial;
    }

    public void Clear() => gameObject.SetActive(false);

    /// <summary>Shows the panel at <paramref name="width"/>; returns its height (0 if there is no HUD panel to copy).</summary>
    public float Build(GuidebookIntegrityPanel state, float width)
    {
        gameObject.SetActive(true);
        if (!EnsureCopy())
        {
            gameObject.SetActive(false);
            return 0f;
        }

        Vector2 size = _copy.rect.size;
        float scale = size.x > 0f ? width / size.x : 1f;
        _copy.localScale = new Vector3(scale, scale, 1f);
        Rect.sizeDelta = new Vector2(width, size.y * scale);

        SetCount(_fenceCount,    state.FenceCount);
        SetCount(_trashCount,    state.TrashCount);
        SetCount(_graffitiCount, state.GraffitiCount);
        if (_bar != null) _bar.ShowPreview(state.Percent / 100f);

        LayoutRebuilder.ForceRebuildLayoutImmediate(_copy);
        return size.y * scale;
    }

    private static void SetCount(TMP_Text text, int count)
    {
        if (text != null) text.text = count.ToString();
    }

    private bool EnsureCopy()
    {
        if (_copy != null) return true;
        if (_searched) return false;
        _searched = true;

        CheckpointIntegrityBar source = FindFirstObjectByType<CheckpointIntegrityBar>(FindObjectsInactive.Include);
        if (source == null)
        {
            Debug.LogWarning("[GuidebookIntegrityPanelView] No CheckpointIntegrityBar in the scene to copy; the guidebook panel is hidden.", this);
            return false;
        }

        _copy = GuidebookHudCopy.Create(source.gameObject, transform, _style, _imageMaterial,
            keep: mb => mb is CheckpointIntegrityBar,
            beforeEnable: copy =>
            {
                _bar = copy.GetComponent<CheckpointIntegrityBar>();
                if (_bar != null) _bar.MarkAsPreview();

                // Counters: always show every row (the HUD may hide finished categories).
                _fenceCount    = FindCount(copy.transform, "Fence Row");
                _trashCount    = FindCount(copy.transform, "Trash Row");
                _graffitiCount = FindCount(copy.transform, "Graffiti Row");
            });
        return true;
    }

    private static TMP_Text FindCount(Transform root, string rowName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != rowName) continue;
            t.gameObject.SetActive(true);
            Transform count = t.Find("Count");
            return count != null ? count.GetComponent<TMP_Text>() : null;
        }
        return null;
    }
}
