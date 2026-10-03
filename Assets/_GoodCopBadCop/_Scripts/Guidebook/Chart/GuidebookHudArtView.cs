using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>HUD element printed in a guidebook page's image slot.</summary>
public enum GuidebookHudArt
{
    None,
    HealthBar,
    GeigerCounter,
}

/// <summary>Example readings shown by the guidebook's printed copies of the health bar and Geiger counter.</summary>
[Serializable]
public class GuidebookSurvivalExample
{
    [Range(0, 100)] public int HealthPercent = 60;
    [Tooltip("Accumulated radiation shown by the arc and readout.")]
    [Range(0, 100)] public int RadiationPercent = 80;
    [Tooltip("Needle position (current exposure rate): 0 = resting, 1 = pegged.")]
    [Range(0f, 1f)] public float ExposureRate = 0.5f;
}

/// <summary>
/// Prints a copy of the scene's HUD health bar (<see cref="HealthBar"/> and its label group) or
/// Geiger counter (<see cref="GeigerCounterUI"/>) onto a guidebook face, lit like the page
/// (see <see cref="GuidebookHudCopy"/>). Each kind is copied once, on first use, and shows the
/// database's <see cref="GuidebookSurvivalExample"/> readings. Created at runtime by <see cref="GuidebookFaceView"/>.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class GuidebookHudArtView : MonoBehaviour
{
    private TMP_Text _style;
    private Material _imageMaterial;

    private readonly Dictionary<GuidebookHudArt, RectTransform> _copies = new Dictionary<GuidebookHudArt, RectTransform>();
    private readonly HashSet<GuidebookHudArt> _searched = new HashSet<GuidebookHudArt>();

    private Image           _healthFill;
    private TMP_Text        _healthPercent;
    private GeigerCounterUI _geiger;

    private RectTransform Rect => (RectTransform)transform;

    public void Configure(TMP_Text style, Material imageMaterial)
    {
        _style         = style;
        _imageMaterial = imageMaterial;
    }

    public void Clear() => gameObject.SetActive(false);

    /// <summary>
    /// Shows <paramref name="art"/> fitted into <paramref name="width"/> x <paramref name="maxHeight"/>;
    /// returns the height used (0 if the scene has no such HUD element to copy).
    /// </summary>
    public float Build(GuidebookHudArt art, GuidebookSurvivalExample example, float width, float maxHeight)
    {
        gameObject.SetActive(true);
        RectTransform copy = GetCopy(art);
        foreach (KeyValuePair<GuidebookHudArt, RectTransform> kv in _copies)
            if (kv.Value != null) kv.Value.gameObject.SetActive(kv.Key == art);

        if (copy == null)
        {
            gameObject.SetActive(false);
            return 0f;
        }

        ApplyExample(art, example ?? new GuidebookSurvivalExample());

        Rect.sizeDelta = new Vector2(width, 0f);
        float height = GuidebookHudCopy.FitVisible(copy, Rect, width, maxHeight);
        Rect.sizeDelta = new Vector2(width, height);
        return height;
    }

    private void ApplyExample(GuidebookHudArt art, GuidebookSurvivalExample example)
    {
        switch (art)
        {
            case GuidebookHudArt.HealthBar:
                float health = example.HealthPercent / 100f;
                if (_healthFill != null) _healthFill.fillAmount = health;
                if (_healthPercent != null) _healthPercent.text = $"{example.HealthPercent}%";
                break;

            case GuidebookHudArt.GeigerCounter:
                if (_geiger != null)
                    _geiger.ShowPreview(example.RadiationPercent / 100f, example.ExposureRate, MaxRadiation());
                break;
        }
    }

    private static float MaxRadiation()
    {
        PlayerInstance player = PlayerInstance.Instance;
        PlayerRadiation radiation = player != null ? player.PlayerRadiation : null;
        return radiation != null && radiation.MaxRadiation > 0f ? radiation.MaxRadiation : 100f;
    }

    // ── Copies ────────────────────────────────────────────────────────────────

    private RectTransform GetCopy(GuidebookHudArt art)
    {
        if (art == GuidebookHudArt.None) return null;
        if (_copies.TryGetValue(art, out RectTransform existing) && existing != null) return existing;
        if (!_searched.Add(art)) return null;

        RectTransform copy = art == GuidebookHudArt.HealthBar ? CopyHealthBar() : CopyGeigerCounter();
        if (copy == null)
            Debug.LogWarning($"[GuidebookHudArtView] No HUD {art} in the scene to copy; that guidebook illustration is hidden.", this);
        _copies[art] = copy;
        return copy;
    }

    private RectTransform CopyHealthBar()
    {
        HealthBar bar = FindFirstObjectByType<HealthBar>(FindObjectsInactive.Include);
        if (bar == null) return null;

        // Copy the bar's group (HEALTH label, + icon, % readout) unless the bar sits directly on the HUD canvas.
        Transform parent = bar.transform.parent;
        GameObject source = parent is RectTransform && parent.GetComponent<Canvas>() == null
            ? parent.gameObject
            : bar.gameObject;

        return GuidebookHudCopy.Create(source, transform, _style, _imageMaterial,
            beforeEnable: copy =>
            {
                foreach (Image img in copy.GetComponentsInChildren<Image>(true))
                    if (img.type == Image.Type.Filled) { _healthFill = img; break; }

                foreach (TMP_Text text in copy.GetComponentsInChildren<TMP_Text>(true))
                    if (text.text != null && text.text.TrimEnd().EndsWith("%")) { _healthPercent = text; break; }
            });
    }

    private RectTransform CopyGeigerCounter()
    {
        GeigerCounterUI geiger = FindFirstObjectByType<GeigerCounterUI>(FindObjectsInactive.Include);
        if (geiger == null) return null;

        return GuidebookHudCopy.Create(geiger.gameObject, transform, _style, _imageMaterial,
            keep: mb => mb is GeigerCounterUI,
            beforeEnable: copy =>
            {
                _geiger = copy.GetComponent<GeigerCounterUI>();
                if (_geiger != null) _geiger.MarkAsPreview();
            });
    }
}
