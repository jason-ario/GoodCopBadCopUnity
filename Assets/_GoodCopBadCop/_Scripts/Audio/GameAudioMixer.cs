using GoodCopBadCop.VoiceChat;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

namespace GoodCopBadCop.Audio
{
    public enum EAudioMixChannel
    {
        Music,
        Sfx
    }

    /// <summary>
    /// Owns the MainMix AudioMixer: Master (headroom + limiter + Dissonance AEC capture) with
    /// Music, SFX and VoiceChat groups. VoiceChat sidechains a Duck Volume on Music so music dips
    /// while anyone talks.
    ///
    /// Routing: most project AudioSources have no output group, so they bypass any mixer and sum
    /// unprotected at the listener (voice + music clipped). This component assigns every
    /// unrouted AudioSource to SFX (voice playback to VoiceChat, music sources to Music) and
    /// re-parents the legacy effect mixers (Voices, Loud Speaker, SFX, NewAudioMixer) into SFX,
    /// so everything passes through the Master limiter.
    ///
    /// Volume: Music/SFX settings drive the mixer faders. Master stays on AudioListener.volume
    /// and voice volume stays on <see cref="VoicePlaybackGain"/> (it can boost above unity).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameAudioMixer : MonoBehaviour
    {
        private const string MusicVolumeParameter = "MusicVolume";
        private const string SfxVolumeParameter = "SfxVolume";
        private const float MinDecibels = -80f;

        private static float musicPercent = 70f;
        private static float sfxPercent = 80f;

        [SerializeField] private AudioMixer _mixer;
        [SerializeField] private AudioMixerGroup _musicGroup;
        [SerializeField] private AudioMixerGroup _sfxGroup;
        [SerializeField] private AudioMixerGroup _voiceChatGroup;

        [Tooltip("Seconds between sweeps that route newly spawned AudioSources into the mixer.")]
        [SerializeField] private float _routingSweepInterval = 0.25f;

        private float nextSweepTime;
        private bool started;

        public static GameAudioMixer Instance { get; private set; }

        /// <summary>Sets a channel volume from a 0-100 settings value. Safe to call before the mixer exists.</summary>
        public static void SetVolumePercent(EAudioMixChannel channel, float percent)
        {
            if (channel == EAudioMixChannel.Music)
            {
                musicPercent = percent;
            }
            else
            {
                sfxPercent = percent;
            }

            if (Instance != null && Instance.started)
            {
                Instance.ApplyVolumes();
            }
        }

        /// <summary>Routes <paramref name="source"/> into the mixer immediately (use for sources that play on spawn).</summary>
        public static void Route(AudioSource source)
        {
            if (Instance != null)
            {
                Instance.RouteSource(source);
            }
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }

            Instance = this;
        }

        private void OnEnable()
        {
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private void OnDisable()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
        }

        private void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void Start()
        {
            // AudioMixer.SetFloat is ignored during Awake, so the first apply happens here.
            started = true;
            ApplyVolumes();
            RouteAllSources();
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSweepTime)
            {
                return;
            }

            nextSweepTime = Time.unscaledTime + _routingSweepInterval;
            RouteAllSources();
        }

        private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            RouteAllSources();
        }

        private void ApplyVolumes()
        {
            if (_mixer == null)
            {
                return;
            }

            _mixer.SetFloat(MusicVolumeParameter, PercentToDecibels(musicPercent));
            _mixer.SetFloat(SfxVolumeParameter, PercentToDecibels(sfxPercent));
        }

        private void RouteAllSources()
        {
            if (_sfxGroup == null)
            {
                return;
            }

            AudioSource[] sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            foreach (AudioSource source in sources)
            {
                RouteSource(source);
            }
        }

        private void RouteSource(AudioSource source)
        {
            if (source == null || _sfxGroup == null)
            {
                return;
            }

            AudioMixerGroup current = source.outputAudioMixerGroup;
            if (current == null)
            {
                source.outputAudioMixerGroup = ResolveGroup(source);
                return;
            }

            // Legacy effect mixers output straight to the listener; chain them into SFX.
            AudioMixer owner = current.audioMixer;
            if (owner != null && owner != _mixer && owner.outputAudioMixerGroup == null)
            {
                owner.outputAudioMixerGroup = _sfxGroup;
            }
        }

        private AudioMixerGroup ResolveGroup(AudioSource source)
        {
            if (_voiceChatGroup != null && source.GetComponent<VoicePlaybackGain>() != null)
            {
                return _voiceChatGroup;
            }

            if (_musicGroup != null && IsMusicSource(source))
            {
                return _musicGroup;
            }

            return _sfxGroup;
        }

        private static bool IsMusicSource(AudioSource source)
        {
            if (source.GetComponent<global::MusicManager>() != null)
            {
                return true;
            }

            global::SFXController sfx = global::SFXController.Instance;
            return sfx != null && sfx.MusicSource == source;
        }

        private static float PercentToDecibels(float percent)
        {
            float linear = Mathf.Clamp01(percent / 100f);
            return linear <= 0.0001f ? MinDecibels : Mathf.Max(MinDecibels, 20f * Mathf.Log10(linear));
        }
    }
}
