using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A networked interactable radio that behaves like a live station.
/// The station starts the moment the radio is first turned on and keeps
/// advancing through songs regardless of whether the radio is on or off.
/// Turning it off simply mutes the local AudioSource; turning it back on
/// re-syncs to the station's current playback position and unmutes.
/// All timing is driven by ServerTime so every client stays in sync.
/// </summary>
public class Radio : Interactable
{
    [SerializeField] private AudioClip[] songs;
    [SerializeField] private AudioSource audioSource;

    // Whether the radio is currently muted (off) or audible (on).
    private readonly NetworkVariable<bool> _isOn = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Set to true the first time the radio is switched on. The station never stops after this.
    private readonly NetworkVariable<bool> _stationRunning = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server-only shuffled playlist queue.
    private int[] _shuffledQueue;
    private int   _queuePosition;

    // Index of the song the station is currently on.
    private readonly NetworkVariable<int> _currentSongIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // Server network time at which the current song started. Used to seek clients to the right position.
    private readonly NetworkVariable<double> _songStartNetworkTime = new NetworkVariable<double>(
        0d,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    // -------------------------------------------------------------------------
    // Netcode lifecycle
    // -------------------------------------------------------------------------

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        _isOn.OnValueChanged             += OnIsOnChanged;
        _currentSongIndex.OnValueChanged += OnSongIndexChanged;

        // Sync late-joining clients: start playing at the correct position, then
        // apply the mute state so off-clients still hear nothing.
        if (_stationRunning.Value && _currentSongIndex.Value >= 0)
        {
            PlayAtCurrentNetworkPosition(_currentSongIndex.Value);
            audioSource.mute = !_isOn.Value;
        }
    }

    public override void OnNetworkDespawn()
    {
        _isOn.OnValueChanged             -= OnIsOnChanged;
        _currentSongIndex.OnValueChanged -= OnSongIndexChanged;
    }

    // -------------------------------------------------------------------------
    // Update — server only, checks when the current song finishes
    // -------------------------------------------------------------------------

    private void Update()
    {
        if (!IsServer || !_stationRunning.Value) return;
        if (songs == null || songs.Length == 0) return;
        if (NetworkManager.Singleton == null) return;

        int idx = _currentSongIndex.Value;
        if (idx < 0 || idx >= songs.Length || songs[idx] == null) return;

        double elapsed = NetworkManager.Singleton.ServerTime.Time - _songStartNetworkTime.Value;
        if (elapsed >= songs[idx].length)
            PickNextSong();
    }

    // -------------------------------------------------------------------------
    // Interaction
    // -------------------------------------------------------------------------

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (IsServer)
            HandleToggleServer();
        else
            RequestToggleServerRpc();
    }

    // -------------------------------------------------------------------------
    // Server logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Handles toggling the radio. Starts the station on first use,
    /// then simply flips the mute state on subsequent interactions.
    /// </summary>
    private void HandleToggleServer()
    {
        if (!_stationRunning.Value)
        {
            // First press — start the station and immediately unmute.
            _stationRunning.Value = true;
            _isOn.Value           = true;
            PickNextSong();
        }
        else
        {
            // Station is already running; just toggle the audible state.
            _isOn.Value = !_isOn.Value;
        }
    }

    /// <summary>
    /// Advances to the next song in the shuffled queue. Re-shuffles when the
    /// queue is exhausted, ensuring no song repeats until all others have played.
    /// </summary>
    private void PickNextSong()
    {
        if (songs == null || songs.Length == 0) return;

        // Build or advance the queue.
        if (_shuffledQueue == null || _queuePosition >= _shuffledQueue.Length)
            BuildShuffledQueue();

        _songStartNetworkTime.Value = NetworkManager.Singleton.ServerTime.Time;
        _currentSongIndex.Value     = _shuffledQueue[_queuePosition++];
    }

    /// <summary>
    /// Builds a new shuffled queue using a Fisher-Yates shuffle.
    /// Avoids placing the last-played song first so there is no immediate repeat
    /// when the queue wraps around.
    /// </summary>
    private void BuildShuffledQueue()
    {
        int count = songs.Length;
        _shuffledQueue = new int[count];

        for (int i = 0; i < count; i++)
            _shuffledQueue[i] = i;

        // Fisher-Yates shuffle.
        for (int i = count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_shuffledQueue[i], _shuffledQueue[j]) = (_shuffledQueue[j], _shuffledQueue[i]);
        }

        // If the first entry in the new queue matches the song that just finished,
        // swap it with the last entry to prevent an immediate repeat.
        if (count > 1 && _shuffledQueue[0] == _currentSongIndex.Value)
            (_shuffledQueue[0], _shuffledQueue[count - 1]) = (_shuffledQueue[count - 1], _shuffledQueue[0]);

        _queuePosition = 0;
    }

    // -------------------------------------------------------------------------
    // RPCs
    // -------------------------------------------------------------------------

    /// <summary>
    /// Relays a toggle request from a non-host client to the server.
    /// </summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleServerRpc()
    {
        HandleToggleServer();
    }

    // -------------------------------------------------------------------------
    // NetworkVariable callbacks
    // -------------------------------------------------------------------------

    private void OnIsOnChanged(bool oldValue, bool newValue)
    {
        if (newValue)
        {
            // The AudioSource keeps running while muted, so only re-sync if the clip
            // actually stopped (e.g. it finished while the radio was off).
            if (!audioSource.isPlaying && _currentSongIndex.Value >= 0)
                PlayAtCurrentNetworkPosition(_currentSongIndex.Value);

            audioSource.mute = false;
        }
        else
        {
            audioSource.mute = true;
        }
    }

    private void OnSongIndexChanged(int oldValue, int newValue)
    {
        if (newValue < 0) return;

        // Play the new song at the correct position; respect the current mute state.
        PlayAtCurrentNetworkPosition(newValue);
        audioSource.mute = !_isOn.Value;
    }

    // -------------------------------------------------------------------------
    // Local audio helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads the clip at <paramref name="songIndex"/>, seeks to the position
    /// the station is currently at based on ServerTime, and starts playback.
    /// </summary>
    private void PlayAtCurrentNetworkPosition(int songIndex)
    {
        if (audioSource == null) return;
        if (songs == null || songIndex < 0 || songIndex >= songs.Length) return;

        AudioClip clip = songs[songIndex];
        if (clip == null) return;

        double elapsed  = NetworkManager.Singleton.ServerTime.Time - _songStartNetworkTime.Value;
        float  seekTime = Mathf.Clamp((float)elapsed, 0f, clip.length - 0.05f);

        audioSource.clip = clip;
        audioSource.loop = false;
        audioSource.time = seekTime;
        audioSource.Play();
    }
}
