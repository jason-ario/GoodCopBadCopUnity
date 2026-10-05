using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Screen-top boss health bar, local to each peer. It builds itself at runtime, so no scene or
/// prefab setup is needed. <see cref="MutantEnemy"/> registers itself while flagged as a boss
/// (<see cref="MutantEnemy.IsBoss"/>, set by <see cref="MutantBreachManager"/> for breach presets
/// with <see cref="MutantBreachData.showUniqueMutantBossHealthBar"/>) and unregisters on despawn.
/// Reads the replicated <see cref="MutantEnemy.BossHealthNormalized"/> every frame. Fades out
/// shortly after the boss reaches 0 (killed or fled) or despawns.
/// </summary>
public class BossHealthBarUI : MonoBehaviour
{
    private const float BarWidth = 720f;
    private const float BarHeight = 22f;
    private const float TopMargin = 48f;
    private const float FadeSpeed = 3f;
    private const float HideDelayAfterDeath = 1.5f;
    private const float TrailDelay = 0.35f;
    private const float TrailSpeed = 0.6f;

    private static BossHealthBarUI _instance;
    private static readonly List<MutantEnemy> _bosses = new List<MutantEnemy>();

    private CanvasGroup _group;
    private RectTransform _fill;
    private RectTransform _trail;
    private TextMeshProUGUI _label;

    private MutantEnemy _current;
    private float _shown = 1f;
    private float _trailValue = 1f;
    private float _trailHoldUntil;
    private float _zeroSince = -1f;

    public static void Register(MutantEnemy boss)
    {
        if (boss == null) return;
        if (!_bosses.Contains(boss)) _bosses.Add(boss);
        EnsureInstance();
    }

    public static void Unregister(MutantEnemy boss)
    {
        _bosses.Remove(boss);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _instance = null;
        _bosses.Clear();
    }

    private static void EnsureInstance()
    {
        if (_instance != null) return;

        var go = new GameObject("Boss Health Bar UI");
        DontDestroyOnLoad(go);
        _instance = go.AddComponent<BossHealthBarUI>();
        _instance.Build();
    }

    private void Build()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;

        var scaler = gameObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        _group = gameObject.AddComponent<CanvasGroup>();
        _group.alpha = 0f;
        _group.interactable = false;
        _group.blocksRaycasts = false;

        RectTransform root = CreateRect("Bar", transform);
        root.anchorMin = root.anchorMax = new Vector2(0.5f, 1f);
        root.pivot = new Vector2(0.5f, 1f);
        root.sizeDelta = new Vector2(BarWidth, BarHeight);
        root.anchoredPosition = new Vector2(0f, -TopMargin - 30f);

        RectTransform border = CreateImage("Border", root, new Color(0f, 0f, 0f, 0.85f));
        Stretch(border, -3f);

        RectTransform background = CreateImage("Background", root, new Color(0.12f, 0.02f, 0.02f, 0.9f));
        Stretch(background, 0f);

        _trail = CreateImage("Trail", root, new Color(0.95f, 0.85f, 0.75f, 0.9f));
        Stretch(_trail, 0f);

        _fill = CreateImage("Fill", root, new Color(0.7f, 0.05f, 0.05f, 1f));
        Stretch(_fill, 0f);

        RectTransform labelRect = CreateRect("Name", root);
        labelRect.anchorMin = new Vector2(0f, 1f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.pivot = new Vector2(0.5f, 0f);
        labelRect.sizeDelta = new Vector2(0f, 36f);
        labelRect.anchoredPosition = new Vector2(0f, 4f);

        _label = labelRect.gameObject.AddComponent<TextMeshProUGUI>();
        _label.alignment = TextAlignmentOptions.Center;
        _label.fontSize = 28f;
        _label.fontStyle = FontStyles.Bold | FontStyles.UpperCase;
        _label.characterSpacing = 8f;
        _label.color = new Color(0.95f, 0.9f, 0.85f, 1f);
        _label.raycastTarget = false;
        _label.text = string.Empty;
    }

    private void Update()
    {
        _bosses.RemoveAll(b => b == null);

        if (_current == null || !_bosses.Contains(_current))
        {
            _current = _bosses.Count > 0 ? _bosses[0] : null;
            if (_current != null) ResetFor(_current);
        }

        float targetAlpha = 0f;

        if (_current != null)
        {
            float health = Mathf.Clamp01(_current.BossHealthNormalized);

            if (health < _shown)
                _trailHoldUntil = Time.unscaledTime + TrailDelay;

            _shown = health;
            SetWidth(_fill, _shown);

            if (_trailValue < _shown)
                _trailValue = _shown;
            else if (Time.unscaledTime >= _trailHoldUntil)
                _trailValue = Mathf.MoveTowards(_trailValue, _shown, TrailSpeed * Time.unscaledDeltaTime);
            SetWidth(_trail, _trailValue);

            if (health <= 0f)
            {
                if (_zeroSince < 0f) _zeroSince = Time.unscaledTime;
            }
            else
            {
                _zeroSince = -1f;
            }

            bool hiding = _zeroSince >= 0f && Time.unscaledTime - _zeroSince >= HideDelayAfterDeath;
            targetAlpha = hiding ? 0f : 1f;
        }

        _group.alpha = Mathf.MoveTowards(_group.alpha, targetAlpha, FadeSpeed * Time.unscaledDeltaTime);
    }

    private void ResetFor(MutantEnemy boss)
    {
        _label.text = boss.BossDisplayName;
        _shown = Mathf.Clamp01(boss.BossHealthNormalized);
        _trailValue = _shown;
        _zeroSince = -1f;
        SetWidth(_fill, _shown);
        SetWidth(_trail, _trailValue);
    }

    private static void SetWidth(RectTransform rect, float normalized)
    {
        rect.anchorMax = new Vector2(Mathf.Clamp01(normalized), 1f);
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        return (RectTransform)go.transform;
    }

    private static RectTransform CreateImage(string name, Transform parent, Color color)
    {
        RectTransform rect = CreateRect(name, parent);
        var image = rect.gameObject.AddComponent<Image>();
        image.color = color;
        image.raycastTarget = false;
        return rect;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }
}
