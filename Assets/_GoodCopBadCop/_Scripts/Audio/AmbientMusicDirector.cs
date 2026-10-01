using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative scheduler for ambient music. The server rolls the silence timer and
/// the random track, then replicates <c>trackIndex</c> + <c>startServerTime</c> so every client
/// (including late joiners) plays the same track at the same position through the local
/// <see cref="AmbientMusicManager"/>. Same ServerTime-seek pattern as <see cref="Radio"/>.
///
/// Lives on a scene-bound NetworkObject (Game Manager) because the persistent Music Manager
/// object is DontDestroyOnLoad and must not carry a NetworkObject across scene reloads.
/// </summary>
public class AmbientMusicDirector : NetworkBehaviour
{
    [Tooltip("Seconds the server schedules a track ahead of 'now' so clients receive it before it starts.")]
    [SerializeField] private float _startLeadTime = 0.5f;

    private readonly NetworkVariable<int> _trackIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private readonly NetworkVariable<double> _trackStartServerTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    private Coroutine _scheduleRoutine;

    // ── Netcode lifecycle ─────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _trackIndex.OnValueChanged += OnTrackIndexChanged;

        // Late joiners: pick up a track that is already in progress.
        if (_trackIndex.Value >= 0)
            AmbientMusicManager.Instance?.PlaySynced(_trackIndex.Value, _trackStartServerTime.Value);

        if (IsServer)
            _scheduleRoutine = StartCoroutine(ServerScheduleLoop());
    }

    public override void OnNetworkDespawn()
    {
        _trackIndex.OnValueChanged -= OnTrackIndexChanged;

        if (_scheduleRoutine != null)
        {
            StopCoroutine(_scheduleRoutine);
            _scheduleRoutine = null;
        }

        AmbientMusicManager.Instance?.StopSynced();
        base.OnNetworkDespawn();
    }

    private void OnTrackIndexChanged(int oldValue, int newValue)
    {
        AmbientMusicManager ambient = AmbientMusicManager.Instance;
        if (ambient == null) return;

        if (newValue >= 0)
            ambient.PlaySynced(newValue, _trackStartServerTime.Value);
        else
            ambient.StopSynced();
    }

    // ── Server schedule ───────────────────────────────────────────────────────

    private IEnumerator ServerScheduleLoop()
    {
        while (true)
        {
            AmbientMusicManager ambient = AmbientMusicManager.Instance;
            if (ambient == null || ambient.TrackCount == 0)
            {
                yield return new WaitForSeconds(5f);
                continue;
            }

            yield return new WaitForSeconds(ambient.NextSilenceDuration());

            // On a host, don't step on encounter music the host is hearing (encounters are
            // broadcast, so this matches what clients hear too).
            while (IsHostEncounterMusicPlaying())
                yield return null;

            int index = Random.Range(0, ambient.TrackCount);
            AudioClip clip = ambient.GetTrack(index);
            if (clip == null) continue;

            double start = NetworkManager.ServerTime.Time + _startLeadTime;

            // Write start time before the index so the index change callback sees the new time.
            _trackStartServerTime.Value = start;
            _trackIndex.Value = index;

            // Hold the track until it finishes or encounter music takes over on the host.
            while (NetworkManager.ServerTime.Time - start < clip.length)
            {
                if (NetworkManager.ServerTime.Time > start + 1.0 && IsHostEncounterMusicPlaying())
                    break;
                yield return null;
            }

            _trackIndex.Value = -1;
        }
    }

    private bool IsHostEncounterMusicPlaying()
    {
        if (!IsClient) return false; // Dedicated server has no local music to check.
        MusicManager music = MusicManager.Instance;
        return music != null && music.IsPlaying && music.CurrentPriority != MusicPriority.Ambient;
    }
}
