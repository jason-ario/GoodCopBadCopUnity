using System;
using System.Collections.Generic;
using GoodCopBadCop.EnvironmentSystem;
using GoodCopBadCop.Infrastructure;
using HighlightPlus;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using VContainer;

/// <summary>
/// State and logic behind the F11 Trailer Console (<see cref="TrailerConsoleUI"/> only renders it).
/// Every change made here is undone by <see cref="ResetAll"/>, which also runs on disable, destroy
/// and application quit — important for the Highlight Plus renderer feature, because toggling it
/// at runtime modifies the <c>PC_Renderer</c> asset itself (and would be saved in the Editor).
/// Hotkeys while the menu is closed: Keypad0 HUD, KeypadPeriod free cam, KeypadEnter freeze,
/// KeypadPlus frame step.
/// </summary>
public class TrailerConsoleActions : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────────────────
    private const int   VolumePriority   = 1000;
    private const float MinFocusDistance = 0.3f;
    private const float MaxFocusDistance = 50f;
    private const float MinAperture      = 1f;
    private const float MaxAperture      = 22f;
    private const string ArmsSocketPath  = "CinemachineCamera/Arms_Socket";

    /// <summary>Time scale presets offered by the console.</summary>
    public static readonly float[] TimeScalePresets = { 1f, 0.5f, 0.25f, 0.1f };

    // ── Serialized ────────────────────────────────────────────────────────────
    [Tooltip("Environment presets offered by the console (all presets in _Data/Environments, minus the schedule).")]
    [SerializeField] private EnvironmentPreset[] _presets = Array.Empty<EnvironmentPreset>();

    [Tooltip("PC_Renderer asset. Its HighlightPlusRenderPassFeature is toggled to hide interactable highlights.")]
    [SerializeField] private UniversalRendererData _pcRendererData;

    [SerializeField] private TrailerFreeCam _freeCam;

    // ── Clean frame state ─────────────────────────────────────────────────────
    private bool _hudHidden;
    private bool _subtitlesHidden;
    private bool _highlightsHidden;
    private ScriptableRendererFeature _highlightFeature;
    private bool _highlightFeatureWasActive;
    private bool _armsHidden;
    private readonly Dictionary<Renderer, bool> _armsRenderers = new();
    private readonly List<Renderer> _rendererBuffer = new();
    private bool _screenFxSuppressed;
    private readonly List<Behaviour> _suppressedFx = new();
    private bool _comfortFxDisabled;
    private PlayerCameraController _comfortCamera;
    private bool _savedRunningFx, _savedHeadBob, _savedCameraShake;
    private bool _godMode;
    private bool _savedHealthInvincible, _savedRadiationInvincible;

    // ── Post FX state ─────────────────────────────────────────────────────────
    private GameObject _volumeGO;
    private VolumeProfile _volumeProfile;
    private DepthOfField _dof;
    private Vignette _vignette;
    private FilmGrain _filmGrain;
    private bool _dofEnabled;
    private bool _vignetteGrainOff;
    private float _focusDistance = 3f;
    private float _aperture = 2.8f;

    // ── Time state ────────────────────────────────────────────────────────────
    private float _defaultFixedDeltaTime;
    private float _timeScale = 1f;
    private bool _frozen;
    private bool _timeOverrideActive;
    private int _frameStepState; // 0 idle, 1 requested, 2 running the stepped frame
    private bool _clockFrozen;
    private ShiftClockController _clock;
    private bool _clockWasEnabled;

    // ── Environment state ─────────────────────────────────────────────────────
    private MainSceneLifetimeScope _envScope;
    private IEnvironmentService _envService;
    private IEnvironmentModel _envModel;

    /// <summary>Raised with a short human-readable status message.</summary>
    public event Action<string> StatusChanged;

    /// <summary>Last posted status message.</summary>
    public string LastStatus { get; private set; } = string.Empty;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity messages
    // ─────────────────────────────────────────────────────────────────────────

    private void Awake()
    {
        _defaultFixedDeltaTime = Time.fixedDeltaTime;
        if (_freeCam == null) _freeCam = GetComponent<TrailerFreeCam>();
    }

    private void OnEnable()
    {
        Application.quitting += ResetAll;
        if (_freeCam != null) _freeCam.StatusChanged += Post;
    }

    private void OnDisable()
    {
        Application.quitting -= ResetAll;
        if (_freeCam != null) _freeCam.StatusChanged -= Post;
        ResetAll();
    }

    private void OnDestroy() => ResetAll();

    private void Update()
    {
        HandleHotkeys();
        TickTime();
    }

    private void LateUpdate()
    {
        if (_armsHidden) ApplyArmsHidden();
    }

    private void HandleHotkeys()
    {
        if (TrailerConsoleUI.IsOpen) return;
        if (GameSettings.Instance == null || !GameSettings.Instance.TrailerConsoleEnabled) return;

        if (Input.GetKeyDown(KeyCode.Keypad0))      SetHudHidden(!_hudHidden);
        if (Input.GetKeyDown(KeyCode.KeypadPeriod)) SetFreeCamActive(!IsFreeCamActive);
        if (Input.GetKeyDown(KeyCode.KeypadEnter))  SetFrozen(!_frozen);
        if (Input.GetKeyDown(KeyCode.KeypadPlus))   FrameStep();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Free cam passthrough
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>The free camera driven by this console (may be null if not wired).</summary>
    public TrailerFreeCam FreeCam => _freeCam;

    /// <summary>True while the free cam owns the view.</summary>
    public bool IsFreeCamActive => _freeCam != null && _freeCam.IsActive;

    /// <summary>Turns the free cam on/off.</summary>
    public void SetFreeCamActive(bool active)
    {
        if (_freeCam == null) { Post("TrailerFreeCam is not wired"); return; }
        _freeCam.SetActive(active);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Clean frame
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True while the HUD is hidden via <see cref="UIController.SetTrailerHudHidden"/>.</summary>
    public bool HudHidden => _hudHidden;

    /// <summary>Hides/shows the HUD (CanvasGroup on playerUI, never SetActive).</summary>
    public void SetHudHidden(bool hidden)
    {
        if (UIController.Instance == null) { Post("No UIController"); return; }
        UIController.Instance.SetTrailerHudHidden(hidden);
        _hudHidden = hidden;
        Post(hidden ? "HUD hidden" : "HUD shown");
    }

    /// <summary>True while all subtitles are hidden.</summary>
    public bool SubtitlesHidden => _subtitlesHidden;

    /// <summary>Hides/shows subtitles (current and future instances).</summary>
    public void SetSubtitlesHidden(bool hidden)
    {
        Subtitles.SetTrailerHidden(hidden);
        _subtitlesHidden = hidden;
        Post(hidden ? "Subtitles hidden" : "Subtitles shown");
    }

    /// <summary>True while interactable highlights are hidden.</summary>
    public bool HighlightsHidden => _highlightsHidden;

    /// <summary>
    /// Hides/shows all Highlight Plus effects by toggling the renderer feature on PC_Renderer.
    /// Per-object highlight state is untouched.
    /// </summary>
    public void SetHighlightsHidden(bool hidden)
    {
        if (hidden == _highlightsHidden) return;

        if (_highlightFeature == null) _highlightFeature = FindHighlightFeature();
        if (_highlightFeature == null) { Post("Highlight feature not found on PC_Renderer"); return; }

        if (hidden)
        {
            _highlightFeatureWasActive = _highlightFeature.isActive;
            _highlightFeature.SetActive(false);
        }
        else
        {
            _highlightFeature.SetActive(_highlightFeatureWasActive);
        }

        _highlightsHidden = hidden;
        Post(hidden ? "Highlights hidden" : "Highlights shown");
    }

    private ScriptableRendererFeature FindHighlightFeature()
    {
        if (_pcRendererData == null) return null;
        foreach (var feature in _pcRendererData.rendererFeatures)
        {
            if (feature is HighlightPlusRenderPassFeature) return feature;
        }
        return null;
    }

    /// <summary>True while the first-person arms / held item are hidden.</summary>
    public bool ArmsHidden => _armsHidden;

    /// <summary>
    /// Hides/shows every renderer under the local player's Arms_Socket (arms and held items).
    /// Re-applied every LateUpdate so newly held items are hidden too.
    /// </summary>
    public void SetArmsHidden(bool hidden)
    {
        if (hidden == _armsHidden) return;
        _armsHidden = hidden;

        if (hidden)
        {
            ApplyArmsHidden();
        }
        else
        {
            foreach (var kv in _armsRenderers)
            {
                if (kv.Key != null) kv.Key.forceRenderingOff = kv.Value;
            }
            _armsRenderers.Clear();
        }

        Post(hidden ? "Arms hidden" : "Arms shown");
    }

    private void ApplyArmsHidden()
    {
        var player = PlayerInstance.Instance;
        if (player == null) return;
        Transform socket = player.transform.Find(ArmsSocketPath);
        if (socket == null) return;

        socket.GetComponentsInChildren(true, _rendererBuffer);
        foreach (var r in _rendererBuffer)
        {
            if (r == null) continue;
            if (!_armsRenderers.ContainsKey(r))
                _armsRenderers[r] = r.forceRenderingOff;
            r.forceRenderingOff = true;
        }
        _rendererBuffer.Clear();
    }

    /// <summary>True while the radiation overlay and glitch screen effects are disabled.</summary>
    public bool ScreenFxSuppressed => _screenFxSuppressed;

    /// <summary>Disables/re-enables <see cref="IrradiatedOverlay"/> and <see cref="GlitchController"/>.</summary>
    public void SetScreenFxSuppressed(bool suppressed)
    {
        if (suppressed == _screenFxSuppressed) return;

        if (suppressed)
        {
            _suppressedFx.Clear();
            var overlay = FindAnyObjectByType<IrradiatedOverlay>();
            if (overlay != null && overlay.enabled) { overlay.enabled = false; _suppressedFx.Add(overlay); }
            var glitch = FindAnyObjectByType<GlitchController>();
            if (glitch != null && glitch.enabled) { glitch.enabled = false; _suppressedFx.Add(glitch); }
        }
        else
        {
            foreach (var fx in _suppressedFx)
            {
                if (fx != null) fx.enabled = true;
            }
            _suppressedFx.Clear();
        }

        _screenFxSuppressed = suppressed;
        Post(suppressed ? "Screen FX off" : "Screen FX restored");
    }

    /// <summary>True while running effects / head bob / camera shake are forced off.</summary>
    public bool ComfortFxDisabled => _comfortFxDisabled;

    /// <summary>Forces the comfort effects off, restoring the player's own settings afterwards.</summary>
    public void SetComfortFxDisabled(bool disabled)
    {
        if (disabled == _comfortFxDisabled) return;

        if (disabled)
        {
            var player = PlayerInstance.Instance;
            _comfortCamera = player != null ? player.GetComponent<PlayerCameraController>() : null;
            if (_comfortCamera == null) { Post("No local player"); return; }

            _savedRunningFx   = _comfortCamera.RunningEffectsEnabled;
            _savedHeadBob     = _comfortCamera.HeadBobEnabled;
            _savedCameraShake = _comfortCamera.CameraShakeEnabled;
            _comfortCamera.SetRunningEffectsEnabled(false);
            _comfortCamera.SetHeadBobEnabled(false);
            _comfortCamera.SetCameraShakeEnabled(false);
        }
        else if (_comfortCamera != null)
        {
            _comfortCamera.SetRunningEffectsEnabled(_savedRunningFx);
            _comfortCamera.SetHeadBobEnabled(_savedHeadBob);
            _comfortCamera.SetCameraShakeEnabled(_savedCameraShake);
            _comfortCamera = null;
        }

        _comfortFxDisabled = disabled;
        Post(disabled ? "Comfort FX off" : "Comfort FX restored");
    }

    /// <summary>True while the local player is invincible via the console.</summary>
    public bool GodMode => _godMode;

    /// <summary>Makes the local player immune to damage and radiation, restoring the previous state when off.</summary>
    public void SetGodMode(bool enabled)
    {
        if (enabled == _godMode) return;
        var player = PlayerInstance.Instance;
        if (player == null) { if (!enabled) _godMode = false; Post("No local player"); return; }

        if (enabled)
        {
            _savedHealthInvincible    = player.PlayerHealth != null && player.PlayerHealth.IsInvincible;
            _savedRadiationInvincible = player.PlayerRadiation != null && player.PlayerRadiation.IsInvincible;
            if (player.PlayerHealth != null)    player.PlayerHealth.IsInvincible = true;
            if (player.PlayerRadiation != null) player.PlayerRadiation.IsInvincible = true;
        }
        else
        {
            if (player.PlayerHealth != null)    player.PlayerHealth.IsInvincible = _savedHealthInvincible;
            if (player.PlayerRadiation != null) player.PlayerRadiation.IsInvincible = _savedRadiationInvincible;
        }

        _godMode = enabled;
        Post(enabled ? "God mode ON" : "God mode OFF");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Post FX (runtime global Volume)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>True while trailer depth of field is active.</summary>
    public bool DofEnabled => _dofEnabled;

    /// <summary>Bokeh focus distance in meters (0.3–50).</summary>
    public float FocusDistance
    {
        get => _focusDistance;
        set
        {
            _focusDistance = Mathf.Clamp(value, MinFocusDistance, MaxFocusDistance);
            if (_dof != null) _dof.focusDistance.Override(_focusDistance);
        }
    }

    /// <summary>Bokeh aperture f-stop (1–22).</summary>
    public float Aperture
    {
        get => _aperture;
        set
        {
            _aperture = Mathf.Clamp(value, MinAperture, MaxAperture);
            if (_dof != null) _dof.aperture.Override(_aperture);
        }
    }

    /// <summary>Enables/disables the trailer Bokeh depth of field.</summary>
    public void SetDofEnabled(bool enabled)
    {
        EnsureVolume();
        _dofEnabled = enabled;
        _dof.active = enabled;
        Post(enabled ? "DoF ON" : "DoF OFF");
    }

    /// <summary>True while vignette and film grain are overridden to zero.</summary>
    public bool VignetteGrainOff => _vignetteGrainOff;

    /// <summary>Overrides vignette and film grain intensity to 0 (or releases the override).</summary>
    public void SetVignetteGrainOff(bool off)
    {
        EnsureVolume();
        _vignetteGrainOff = off;
        _vignette.active  = off;
        _filmGrain.active = off;
        Post(off ? "Vignette/grain OFF" : "Vignette/grain restored");
    }

    private void EnsureVolume()
    {
        if (_volumeGO != null) return;

        _volumeGO = new GameObject("[TrailerVolume]");
        var volume = _volumeGO.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = VolumePriority;
        volume.weight   = 1f;

        _volumeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _volumeProfile.name = "[TrailerVolumeProfile]";
        volume.sharedProfile = _volumeProfile;

        _dof = _volumeProfile.Add<DepthOfField>();
        _dof.mode.Override(DepthOfFieldMode.Bokeh);
        _dof.focusDistance.Override(_focusDistance);
        _dof.aperture.Override(_aperture);
        _dof.focalLength.Override(50f);
        _dof.active = _dofEnabled;

        _vignette = _volumeProfile.Add<Vignette>();
        _vignette.intensity.Override(0f);
        _vignette.active = _vignetteGrainOff;

        _filmGrain = _volumeProfile.Add<FilmGrain>();
        _filmGrain.intensity.Override(0f);
        _filmGrain.active = _vignetteGrainOff;
    }

    private void DestroyVolume()
    {
        if (_volumeGO != null) Destroy(_volumeGO);
        if (_volumeProfile != null) Destroy(_volumeProfile);
        _volumeGO = null;
        _volumeProfile = null;
        _dof = null;
        _vignette = null;
        _filmGrain = null;
        _dofEnabled = false;
        _vignetteGrainOff = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Time
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Desired time scale while not frozen.</summary>
    public float TimeScale => _timeScale;

    /// <summary>True while time is frozen (scale 0).</summary>
    public bool Frozen => _frozen;

    private float EffectiveScale => _frozen ? 0f : _timeScale;

    /// <summary>Sets the slow-motion time scale (also scales fixedDeltaTime).</summary>
    public void SetTimeScale(float scale)
    {
        _timeScale = Mathf.Clamp(scale, 0.01f, 1f);
        ApplyTimeScaleNow();
        Post($"Time scale x{_timeScale:0.##}");
    }

    /// <summary>Freezes (scale 0) or unfreezes time.</summary>
    public void SetFrozen(bool frozen)
    {
        _frozen = frozen;
        _frameStepState = 0;
        ApplyTimeScaleNow();
        Post(frozen ? "Time frozen" : $"Time resumed (x{_timeScale:0.##})");
    }

    /// <summary>While frozen, advances exactly one frame at scale 1.</summary>
    public void FrameStep()
    {
        if (!_frozen) { Post("Freeze time first"); return; }
        _frameStepState = 1;
        Post("Frame step");
    }

    private bool CanDriveTime =>
        (UIController.Instance == null || !UIController.Instance.IsGameTimeFrozen) && !Input.GetKey(KeyCode.Equals);

    private void ApplyTimeScaleNow()
    {
        float scale = EffectiveScale;
        bool overriding = !Mathf.Approximately(scale, 1f);

        if (!overriding && _timeOverrideActive)
        {
            // Hand time back to the game once.
            if (CanDriveTime) Time.timeScale = 1f;
            Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }

        _timeOverrideActive = overriding;
        if (overriding && CanDriveTime) WriteTimeScale(scale);
    }

    private void TickTime()
    {
        if (!_timeOverrideActive) return;

        // Yield to the pause menu and the DebugConsole fast-forward hold.
        if (!CanDriveTime) return;

        if (_frameStepState == 1)
        {
            WriteTimeScale(1f);
            _frameStepState = 2;
            return;
        }
        if (_frameStepState == 2)
            _frameStepState = 0;

        WriteTimeScale(EffectiveScale);
    }

    private void WriteTimeScale(float scale)
    {
        Time.timeScale = scale;
        Time.fixedDeltaTime = scale > 0f ? _defaultFixedDeltaTime * scale : _defaultFixedDeltaTime;
    }

    /// <summary>True while the shift clock is paused by the console.</summary>
    public bool ClockFrozen => _clockFrozen;

    /// <summary>Pauses/resumes the shift clock. Server/host only.</summary>
    public void SetClockFrozen(bool frozen)
    {
        if (frozen == _clockFrozen) return;

        if (frozen)
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsServer)
            {
                Post("Clock freeze is host only");
                return;
            }
            _clock = FindAnyObjectByType<ShiftClockController>();
            if (_clock == null) { Post("No ShiftClockController"); return; }
            _clockWasEnabled = _clock.enabled;
            _clock.PauseClock();
        }
        else if (_clock != null)
        {
            if (_clockWasEnabled) _clock.ResumeClock();
            _clock = null;
        }

        _clockFrozen = frozen;
        Post(frozen ? "Shift clock frozen" : "Shift clock resumed");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Environment
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>Presets shown in the console's preset list.</summary>
    public IReadOnlyList<EnvironmentPreset> Presets => _presets;

    /// <summary>The environment model's active preset, or null when unavailable.</summary>
    public EnvironmentPreset CurrentPreset =>
        TryGetEnvironment() ? _envModel.CurrentPreset.CurrentValue : null;

    /// <summary>True while scheduled preset changes are locked.</summary>
    public bool PresetLocked => TryGetEnvironment() && _envService.IsPresetLocked;

    /// <summary>The model's rain state.</summary>
    public bool RainEnabled => TryGetEnvironment() && _envModel.CurrentRainEnabled.CurrentValue;

    /// <summary>Applies <see cref="Presets"/>[<paramref name="index"/>] and locks the look.</summary>
    public void ApplyPreset(int index)
    {
        if (_presets == null || index < 0 || index >= _presets.Length || _presets[index] == null)
        {
            Post("Invalid preset");
            return;
        }
        if (!TryGetEnvironment()) { Post("Environment service unavailable"); return; }

        _envService.SetPresetLocked(true);
        _envService.ApplyPreset(_presets[index]);
        Post($"Preset: {_presets[index].name} (locked)");
    }

    /// <summary>Locks/unlocks scheduled preset changes. Unlocking restores the scheduled look.</summary>
    public void SetPresetLocked(bool locked)
    {
        if (!TryGetEnvironment()) { Post("Environment service unavailable"); return; }
        _envService.SetPresetLocked(locked);
        Post(locked ? "Environment locked" : "Environment unlocked (schedule restored)");
    }

    /// <summary>Turns rain on/off and locks the look.</summary>
    public void SetRain(bool enabled)
    {
        if (!TryGetEnvironment()) { Post("Environment service unavailable"); return; }
        _envService.SetPresetLocked(true);
        _envService.SetRainEnabled(enabled);
        Post(enabled ? "Rain ON (locked)" : "Rain OFF (locked)");
    }

    /// <summary>Triggers a lightning strike if a <see cref="LightningEffect"/> exists.</summary>
    public void StrikeLightning()
    {
        var lightning = FindAnyObjectByType<LightningEffect>();
        if (lightning == null) { Post("No LightningEffect in scene"); return; }
        lightning.StrikeNow();
        Post("Lightning!");
    }

    private bool TryGetEnvironment()
    {
        if (_envScope != null && _envService != null && _envModel != null) return true;

        _envService = null;
        _envModel = null;
        _envScope = FindAnyObjectByType<MainSceneLifetimeScope>();
        if (_envScope == null || _envScope.Container == null) return false;

        IObjectResolver container = _envScope.Container;
        if (!container.TryResolve(out _envService) || !container.TryResolve(out _envModel))
        {
            _envService = null;
            _envModel = null;
            return false;
        }
        return true;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Macros
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>One-click clean frame: HUD, subtitles, highlights, screen FX, comfort FX off + god mode.</summary>
    public void TrailerMode()
    {
        SetHudHidden(true);
        SetSubtitlesHidden(true);
        SetHighlightsHidden(true);
        SetScreenFxSuppressed(true);
        SetComfortFxDisabled(true);
        SetGodMode(true);
        Post("Trailer Mode ON");
    }

    /// <summary>Undoes every change made by the console.</summary>
    public void ResetAll()
    {
        if (_freeCam != null) _freeCam.SetActive(false);

        // Time
        bool hadTimeOverride = _timeOverrideActive || _frozen || !Mathf.Approximately(_timeScale, 1f);
        _frozen = false;
        _timeScale = 1f;
        _frameStepState = 0;
        _timeOverrideActive = false;
        if (hadTimeOverride)
        {
            if (UIController.Instance == null || !UIController.Instance.IsGameTimeFrozen)
                Time.timeScale = 1f;
            if (_defaultFixedDeltaTime > 0f) Time.fixedDeltaTime = _defaultFixedDeltaTime;
        }
        SetClockFrozen(false);

        // Renderer feature first — it persists on the asset.
        SetHighlightsHidden(false);

        // Environment
        if (_envService != null && _envService.IsPresetLocked) _envService.SetPresetLocked(false);

        // Clean frame
        if (_hudHidden && UIController.Instance != null) UIController.Instance.SetTrailerHudHidden(false);
        _hudHidden = false;
        if (_subtitlesHidden) Subtitles.SetTrailerHidden(false);
        _subtitlesHidden = false;
        SetArmsHidden(false);
        SetScreenFxSuppressed(false);
        SetComfortFxDisabled(false);
        SetGodMode(false);
        _godMode = false;

        DestroyVolume();
        Post("All trailer changes reset");
    }

    private void Post(string message)
    {
        LastStatus = message;
        StatusChanged?.Invoke(message);
    }
}
