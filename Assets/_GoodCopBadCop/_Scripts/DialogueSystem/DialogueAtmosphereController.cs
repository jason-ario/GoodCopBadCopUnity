using GoodCopBadCop.Settings;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Local-only screen-space mood for conversations. Each frame it checks who the local player is
/// talking to (<see cref="OverhearRange.TryGetLocalConversationPartner"/>); if that speaker has a
/// <see cref="DialogueAtmosphereSource"/>, its <see cref="DialogueAtmosphereProfile"/> fades in as a
/// pulsing vignette and fades out when the conversation ends or the partner changes.
///
/// Uses its own runtime global Volume, with priority below <c>DamageVignetteView</c> so hit/drunk
/// pulses still layer on top. The vignette is blended by hand from the captured base look (the
/// volume stays at full weight while active), so it never lightens a stronger base vignette.
/// Lives on <c>/--- SETUP/Dialogue System</c> next to <see cref="ConversationNameTag"/>.
/// </summary>
public class DialogueAtmosphereController : MonoBehaviour
{
    // Above the base "Post Processing" volume, below DamageVignetteView (100).
    private const float VolumePriority = 90f;

    private Volume _volume;
    private VolumeProfile _runtimeProfile;
    private Vignette _vignette;
    private ColorAdjustments _colorAdjustments;

    private DialogueAtmosphereProfile _active;   // profile currently driving the look (incl. while fading out)
    private float _fade;                          // 0..1 blend toward the profile
    private float _pulseTime;

    private Color _baseColor = Color.black;
    private float _baseIntensity;
    private float _baseSmoothness = 0.2f;
    private float _baseSaturation;

    private SpeakingInteraction _cachedPartner;
    private DialogueAtmosphereProfile _cachedPartnerProfile;

    private void Awake()
    {
        BuildVolume();
    }

    private void OnDisable()
    {
        _fade = 0f;
        _active = null;
        if (_volume != null) _volume.weight = 0f;
    }

    private void OnDestroy()
    {
        if (_runtimeProfile != null) Destroy(_runtimeProfile);
        if (_volume != null) Destroy(_volume.gameObject);
    }

    private void BuildVolume()
    {
        var go = new GameObject("Dialogue Atmosphere Volume");
        go.transform.SetParent(transform, false);

        // Match the base volume's layer so the camera's Volume Mask includes it.
        var baseAnchor = FindAnyObjectByType<GraphicsPreferencesVolumeAnchor>();
        go.layer = baseAnchor != null ? baseAnchor.gameObject.layer : gameObject.layer;

        _runtimeProfile = ScriptableObject.CreateInstance<VolumeProfile>();
        _runtimeProfile.name = "Dialogue Atmosphere Profile (Runtime)";
        _vignette = _runtimeProfile.Add<Vignette>();
        _colorAdjustments = _runtimeProfile.Add<ColorAdjustments>();
        _colorAdjustments.active = false;

        _volume = go.AddComponent<Volume>();
        _volume.isGlobal = true;
        _volume.priority = VolumePriority;
        _volume.weight = 0f;
        _volume.sharedProfile = _runtimeProfile;
    }

    private void Update()
    {
        if (_volume == null) return;

        DialogueAtmosphereProfile desired = ResolveDesiredProfile();

        // Switching between two atmospheres: fade the old one out fully before the new one starts.
        if (desired != null && _active == null)
        {
            BeginProfile(desired);
        }
        else if (desired != null && desired != _active && _fade <= 0f)
        {
            BeginProfile(desired);
        }

        if (_active == null) return;

        float target = desired == _active ? 1f : 0f;
        float duration = target > _fade ? _active.FadeInDuration : _active.FadeOutDuration;
        _fade = duration > 0f ? Mathf.MoveTowards(_fade, target, Time.unscaledDeltaTime / duration) : target;
        _pulseTime += Time.unscaledDeltaTime;

        if (_fade <= 0f && target <= 0f)
        {
            _volume.weight = 0f;
            _active = null;
            return;
        }

        Apply(_active);
    }

    private DialogueAtmosphereProfile ResolveDesiredProfile()
    {
        if (!OverhearRange.TryGetLocalConversationPartner(out SpeakingInteraction partner))
        {
            _cachedPartner = null;
            _cachedPartnerProfile = null;
            return null;
        }

        if (partner != _cachedPartner)
        {
            _cachedPartner = partner;
            var source = partner.GetComponentInParent<DialogueAtmosphereSource>(true);
            _cachedPartnerProfile = source != null ? source.Profile : null;
        }

        return _cachedPartnerProfile;
    }

    private void BeginProfile(DialogueAtmosphereProfile profile)
    {
        _active = profile;
        _fade = 0f;
        _pulseTime = 0f;
        CaptureBaseVignette();

        bool useSaturation = profile.Saturation < 0f;
        _colorAdjustments.active = useSaturation;
        _colorAdjustments.saturation.overrideState = useSaturation;
        _colorAdjustments.saturation.value = profile.Saturation;
        // Only saturation is ever overridden; post exposure belongs to the Brightness preference.
        _colorAdjustments.postExposure.overrideState = false;
        _colorAdjustments.colorFilter.overrideState = false;
        _colorAdjustments.contrast.overrideState = false;
        _colorAdjustments.hueShift.overrideState = false;
    }

    /// <summary>Samples the underlying vignette while our volume is idle so we blend from it.</summary>
    private void CaptureBaseVignette()
    {
        _volume.weight = 0f;
        VolumeStack stack = VolumeManager.instance != null ? VolumeManager.instance.stack : null;
        ColorAdjustments baseColor = stack != null ? stack.GetComponent<ColorAdjustments>() : null;
        _baseSaturation = baseColor != null && baseColor.active ? baseColor.saturation.value : 0f;

        Vignette baseVignette = stack != null ? stack.GetComponent<Vignette>() : null;
        if (baseVignette == null || !baseVignette.active)
        {
            _baseColor = Color.black;
            _baseIntensity = 0f;
            _baseSmoothness = 0.2f;
            return;
        }

        _baseColor = baseVignette.color.value;
        _baseIntensity = baseVignette.intensity.value;
        _baseSmoothness = baseVignette.smoothness.value;
    }

    private void Apply(DialogueAtmosphereProfile profile)
    {
        float pulse = profile.EvaluatePulse(_pulseTime);
        float targetIntensity = Mathf.Lerp(profile.MinIntensity, profile.MaxIntensity, pulse);
        // Never go below the base vignette: the mood only ever darkens.
        targetIntensity = Mathf.Max(targetIntensity, _baseIntensity);

        Color color = Color.Lerp(_baseColor, profile.VignetteColor, _fade);
        color.a = 1f;

        _vignette.active = true;
        _vignette.color.Override(color);
        _vignette.intensity.Override(Mathf.Lerp(_baseIntensity, targetIntensity, _fade));
        _vignette.smoothness.Override(Mathf.Lerp(_baseSmoothness, profile.Smoothness, _fade));

        // Everything is blended by hand from the captured base look at full volume weight.
        _volume.weight = 1f;
        if (_colorAdjustments.active)
            _colorAdjustments.saturation.value = Mathf.Lerp(_baseSaturation, _baseSaturation + profile.Saturation, _fade);
    }
}
