using GoodCopBadCop.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Low-health feedback for the HUD subject (local player, or the watched teammate while spectating):
/// a slowly pulsing red post-processing vignette plus a looping bottom-centre text alert (via
/// <see cref="UIController.ShowLowHealthAlert"/>, same style as the radiation/shipment alerts).
///
/// The vignette lives on its own runtime global Volume and is blended manually from the base
/// volume's vignette (same approach as <see cref="PostProcessAlarmController"/>), so the rest of
/// the grade is untouched. Its priority sits below the damage-hit channels
/// (<see cref="GoodCopBadCop.Effects.DamageVignetteView"/>) so hit flashes still read on top.
///
/// Everything is suppressed while the subject is dead, the pause menu is open, or the main menu
/// is showing — mirrors <see cref="RadiationAlertUI"/>.
/// </summary>
public class LowHealthAlertUI : MonoBehaviour
{
    private const float VolumePriority = 90f;

    [Header("Threshold")]
    [Tooltip("Normalized health (0-1) at or below which the low-health feedback is shown.")]
    [SerializeField, Range(0f, 1f)] private float _lowHealthThreshold = 0.3f;

    [Header("Alert")]
    [SerializeField] private string _alertMessage = "Health low. Eat or smoke to heal.";

    [Header("Vignette Pulse")]
    [SerializeField] private Color _vignetteColor = new Color(0.75f, 0.02f, 0.02f, 1f);

    [Tooltip("Pulses per second. Kept slow so it reads as a heartbeat, not an alarm.")]
    [SerializeField, Min(0.01f)] private float _pulseSpeed = 0.55f;

    [Tooltip("Vignette intensity at the trough of each pulse, right at the threshold.")]
    [SerializeField, Range(0f, 1f)] private float _minIntensity = 0.3f;

    [Tooltip("Vignette intensity at the peak of each pulse, right at the threshold.")]
    [SerializeField, Range(0f, 1f)] private float _maxIntensity = 0.42f;

    [Tooltip("Extra intensity added to both trough and peak as health approaches zero.")]
    [SerializeField, Range(0f, 0.5f)] private float _criticalIntensityBonus = 0.1f;

    [SerializeField, Range(0.01f, 1f)] private float _smoothness = 0.65f;

    [Tooltip("Seconds to fade the vignette in/out when entering/leaving low health.")]
    [SerializeField, Min(0.01f)] private float _fadeDuration = 0.6f;

    private PlayerInstance _subscribedInstance;
    private PlayerHealth _playerHealth;

    private bool _healthIsLow;
    private float _severity; // 0 at threshold, 1 at zero health
    private bool _alertShown;
    private bool _effectActive;

    private Volume _volume;
    private VolumeProfile _profile;
    private Vignette _vignette;
    private GraphicsPreferencesVolumeAnchor _baseAnchor;
    private float _presence; // 0..1 fade in/out factor

    private void OnEnable()
    {
        UIController.OnPauseMenuOpened += RefreshVisibility;
        SubscribeTo(SpectateManager.HudSubject);
    }

    private void OnDisable()
    {
        UIController.OnPauseMenuOpened -= RefreshVisibility;
        SubscribeTo(null);
        ForceHide();
    }

    private void OnDestroy()
    {
        if (_volume != null)
            Destroy(_volume.gameObject);
        if (_profile != null)
            Destroy(_profile);
    }

    private void Update()
    {
        // Death/respawn keeps the old corpse PlayerInstance alive (see RadiationAlertUI), and
        // spectating swaps the HUD subject, so re-check the subject every frame.
        PlayerInstance subject = SpectateManager.HudSubject;
        if (subject != _subscribedInstance)
            SubscribeTo(subject);

        RefreshVisibility();
        UpdateVignette();
    }

    private void SubscribeTo(PlayerInstance playerInstance)
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnHealthChanged -= HandleHealthChanged;
            _playerHealth.OnDeath -= HandleHealthChanged;
            _playerHealth.OnRespawn -= HandleHealthChanged;
            _playerHealth = null;
        }

        _healthIsLow = false;
        _subscribedInstance = playerInstance;

        if (playerInstance != null && playerInstance.PlayerHealth != null)
        {
            _playerHealth = playerInstance.PlayerHealth;
            _playerHealth.OnHealthChanged += HandleHealthChanged;
            _playerHealth.OnDeath += HandleHealthChanged;
            _playerHealth.OnRespawn += HandleHealthChanged;
            HandleHealthChanged();
        }
        else
        {
            RefreshVisibility();
        }
    }

    private void HandleHealthChanged()
    {
        if (_playerHealth == null || _playerHealth.MaxHealth <= 0f || _playerHealth.IsDead)
        {
            _healthIsLow = false;
            _severity = 0f;
        }
        else
        {
            float normalized = _playerHealth.Health / _playerHealth.MaxHealth;
            _healthIsLow = normalized > 0f && normalized <= _lowHealthThreshold;
            _severity = _lowHealthThreshold > 0f ? Mathf.Clamp01(1f - normalized / _lowHealthThreshold) : 1f;
        }

        RefreshVisibility();
    }

    /// <summary>Syncs the text alert and the vignette's target state with the current conditions.</summary>
    private void RefreshVisibility()
    {
        bool isDead = _playerHealth != null && _playerHealth.IsDead;
        bool isPaused = UIController.Instance != null && UIController.Instance.IsPaused;
        bool isMainMenuActive = MainMenuController.Instance != null &&
                                MainMenuController.Instance.mainMenu != null &&
                                MainMenuController.Instance.mainMenu.activeSelf;

        bool shouldShow = _healthIsLow && !isDead && !isPaused && !isMainMenuActive;
        _effectActive = shouldShow;

        if (shouldShow == _alertShown) return;

        _alertShown = shouldShow;

        if (shouldShow)
            UIController.Instance?.ShowLowHealthAlert(_alertMessage);
        else
            UIController.Instance?.HideLowHealthAlert();
    }

    private void ForceHide()
    {
        _effectActive = false;
        _presence = 0f;
        if (_volume != null)
            _volume.weight = 0f;

        if (!_alertShown) return;

        _alertShown = false;
        UIController.Instance?.HideLowHealthAlert();
    }

    private void UpdateVignette()
    {
        float target = _effectActive ? 1f : 0f;
        _presence = Mathf.MoveTowards(_presence, target, Time.unscaledDeltaTime / _fadeDuration);

        if (_presence <= 0f)
        {
            if (_volume != null)
                _volume.weight = 0f;
            return;
        }

        if (!EnsureVolume())
            return;

        // Smoothstepped ping-pong reads as a slow throb rather than a strobe.
        float t = Mathf.PingPong(Time.unscaledTime * _pulseSpeed * 2f, 1f);
        t = t * t * (3f - 2f * t);

        float bonus = _criticalIntensityBonus * _severity;
        float pulseIntensity = Mathf.Lerp(_minIntensity + bonus, _maxIntensity + bonus, t);

        GetBaseVignette(out Color baseColor, out float baseIntensity, out float baseSmoothness);

        _vignette.color.value = Color.Lerp(baseColor, _vignetteColor, _presence);
        _vignette.intensity.value = Mathf.Lerp(baseIntensity, Mathf.Max(baseIntensity, pulseIntensity), _presence);
        _vignette.smoothness.value = Mathf.Lerp(baseSmoothness, _smoothness, _presence);
        _volume.weight = 1f;
    }

    private bool EnsureVolume()
    {
        if (_volume != null)
            return true;

        if (_baseAnchor == null)
            _baseAnchor = FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();

        var go = new GameObject("Low Health Vignette Volume");
        // Match the base volume's layer so it falls inside the camera's Volume Mask.
        if (_baseAnchor != null)
            go.layer = _baseAnchor.gameObject.layer;
        DontDestroyOnLoad(go);

        _profile = ScriptableObject.CreateInstance<VolumeProfile>();
        _profile.name = "Low Health Vignette Profile (Runtime)";
        _vignette = _profile.Add<Vignette>();
        _vignette.active = true;
        _vignette.color.overrideState = true;
        _vignette.intensity.overrideState = true;
        _vignette.smoothness.overrideState = true;

        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight = 0f;
        _volume.sharedProfile = _profile;
        return true;
    }

    private void GetBaseVignette(out Color color, out float intensity, out float smoothness)
    {
        color = Color.black;
        intensity = 0f;
        smoothness = 0.2f;

        if (_baseAnchor == null)
            _baseAnchor = FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();

        Volume baseVolume = _baseAnchor != null ? _baseAnchor.Volume : null;
        if (baseVolume == null) return;

        VolumeProfile baseProfile = baseVolume.HasInstantiatedProfile() ? baseVolume.profile : baseVolume.sharedProfile;
        if (baseProfile == null || !baseProfile.TryGet(out Vignette baseVignette) || !baseVignette.active)
            return;

        if (baseVignette.color.overrideState) color = baseVignette.color.value;
        if (baseVignette.intensity.overrideState) intensity = baseVignette.intensity.value;
        if (baseVignette.smoothness.overrideState) smoothness = baseVignette.smoothness.value;
    }
}
