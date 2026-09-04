using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked intro cutscene for the bus. On spawn the bus idles with ambient audio,
/// then after <see cref="idleDuration"/> seconds it crossfades to the driving SFX,
/// accelerates off-screen along <see cref="driveLocalDirection"/> and is retired —
/// synced for all clients via a ClientRpc.
/// Requires a NetworkObject component on this GameObject.
///
/// Late joiners: this is an IN-SCENE PLACED NetworkObject whose bus visual is authored ACTIVE, so
/// it must stay spawned for its whole lifetime. It used to end the sequence by despawning itself,
/// which meant a client connecting afterwards loaded the scene (instantiating the bus, visual on)
/// but never received the object in its synchronization payload — so <see cref="OnNetworkSpawn"/>
/// never ran on that client, nothing ever called <c>busVisual.SetActive(false)</c>, and the player
/// was left looking at a stationary bus that everyone else had watched drive away. Because no
/// callback of any kind fires on an object that was never spawned, a NetworkVariable could not
/// have rescued that: the object has to remain spawned. It now stays alive with the visual hidden
/// and the audio stopped, and <see cref="_cutsceneFinished"/> tells every peer — including late
/// joiners, via ordinary spawn synchronization — that the bus is already gone.
/// </summary>
[RequireComponent(typeof(NetworkObject))]
public class BusCutsceneController : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private GameObject busVisual;
    [SerializeField] private AudioSource busAudioSource;
    [SerializeField] private MachineShake machineShake;

    [Header("Audio")]
    [SerializeField] private AudioClip driveClip;
    [SerializeField] private float driveRevDelay = 3f;

    [Header("Timing")]
    [SerializeField] private float idleDuration = 4f;
    [SerializeField] private float driveDuration = 6f;

    [Header("Movement")]
    [SerializeField] private float driveSpeed = 8f;
    /// <summary>Direction of travel expressed in this transform's local space.</summary>
    [SerializeField] private Vector3 driveLocalDirection = Vector3.forward;
    /// <summary>
    /// Controls how speed ramps up over <see cref="driveDuration"/>.
    /// X axis = normalised time (0–1), Y axis = speed multiplier (0–1).
    /// Defaults to a smooth ease-in feel.
    /// </summary>
    [SerializeField] private AnimationCurve speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

    /// <summary>
    /// Server-authoritative "the bus has already driven off" flag. Replicated rather than pushed by
    /// RPC so a client that connects after the cutscene has played still learns the bus is gone —
    /// see the class summary for why this cannot be an RPC or a despawn.
    /// </summary>
    private readonly NetworkVariable<bool> _cutsceneFinished = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        // Keep the bus hidden and silent until the first lobby player has actually
        // spawned in, so it doesn't idle on screen before anyone arrives. This also covers the
        // late-joining case, where the scene-authored visual starts active: a joining client hides
        // it here and only ever reveals it if the cutscene is still to come.
        RetireBusVisual();

        _cutsceneFinished.OnValueChanged += OnCutsceneFinishedChanged;

        // Server waits for the first lobby player spawn before revealing the bus
        // and starting the sequence. A late joiner whose _cutsceneFinished is already true simply
        // leaves the bus hidden above — the cutscene is over and must not replay for them.
        if (IsServer)
            PlayerSpawner.OnPlayerSpawnedAtLobby += OnFirstPlayerSpawnedAtLobby;
    }

    public override void OnNetworkDespawn()
    {
        PlayerSpawner.OnPlayerSpawnedAtLobby -= OnFirstPlayerSpawnedAtLobby;

        _cutsceneFinished.OnValueChanged -= OnCutsceneFinishedChanged;
    }

    private void OnCutsceneFinishedChanged(bool previous, bool current)
    {
        if (current) RetireBusVisual();
    }

    /// <summary>Hides the bus and silences it. Safe to call repeatedly and before the sequence runs.</summary>
    private void RetireBusVisual()
    {
        if (busVisual != null)
            busVisual.SetActive(false);

        if (busAudioSource != null)
        {
            busAudioSource.loop = false;
            busAudioSource.Stop();
        }
    }

    private void OnFirstPlayerSpawnedAtLobby(ulong clientId)
    {
        // Unsubscribe immediately so the sequence only starts once.
        PlayerSpawner.OnPlayerSpawnedAtLobby -= OnFirstPlayerSpawnedAtLobby;

        // Defensive: never replay the intro because a player arrived late.
        if (_cutsceneFinished.Value) return;

        RevealBusClientRpc();
        StartCoroutine(ServerSequence());
    }

    /// <summary>Broadcasts to every client (including the host) to show the bus and start idle audio.</summary>
    [ClientRpc]
    private void RevealBusClientRpc()
    {
        if (busVisual != null)
            busVisual.SetActive(true);

        if (busAudioSource != null)
        {
            busAudioSource.loop   = true;
            busAudioSource.volume = 1f;
            if (!busAudioSource.isPlaying)
                busAudioSource.Play();
        }
    }

    // -------------------------------------------------------------------------
    // Server
    // -------------------------------------------------------------------------

    /// <summary>
    /// Server-side: waits for the idle window, signals clients to begin the drive,
    /// then marks the cutscene finished once the full drive duration has elapsed.
    /// </summary>
    private IEnumerator ServerSequence()
    {
        yield return new WaitForSeconds(idleDuration);

        BeginDriveClientRpc();

        // Account for the rev delay + full movement duration, then add a small buffer
        // so clients finish their coroutines cleanly before the bus is retired.
        yield return new WaitForSeconds(driveRevDelay + driveDuration + 0.5f);

        // Deliberately NOT NetworkObject.Despawn(): this is an in-scene placed object, and
        // despawning it means a client that connects later never receives it, never runs
        // OnNetworkSpawn, and is left with the scene-authored bus visible on screen forever.
        // Flipping the replicated flag instead hides it for everyone, now and in the future.
        _cutsceneFinished.Value = true;
        RetireBusVisual();
    }

    // -------------------------------------------------------------------------
    // Clients
    // -------------------------------------------------------------------------

    /// <summary>Broadcasts to every client (including the host) to start the drive sequence.</summary>
    [ClientRpc]
    private void BeginDriveClientRpc()
    {
        StartCoroutine(DriveSequence());
    }

    /// <summary>
    /// Moves the controller (and its Bus child) off-screen using the speed curve
    /// for a smooth acceleration from a standstill.
    /// </summary>
    private IEnumerator DriveSequence()
    {
        // Switch audio immediately when the RPC fires.
        if (busAudioSource != null)
        {
            busAudioSource.loop = false;
            busAudioSource.Stop();

            if (driveClip != null)
            {
                busAudioSource.clip = driveClip;
                busAudioSource.Play();
            }
        }

        // Let the engine rev sound play before the bus starts moving.
        yield return new WaitForSeconds(driveRevDelay);

        // Ramp up shake intensity in step with the acceleration.
        float baseShakeStrength = machineShake != null ? machineShake.positionStrength : 0f;
        const float peakShake   = 0.05f;

        Vector3 worldDirection = transform.TransformDirection(driveLocalDirection.normalized);
        float elapsed = 0f;

        while (elapsed < driveDuration)
        {
            float t             = elapsed / driveDuration;
            float speedFraction = speedCurve.Evaluate(t);

            transform.position += worldDirection * (driveSpeed * speedFraction) * Time.deltaTime;

            if (machineShake != null)
                machineShake.positionStrength = Mathf.Lerp(baseShakeStrength, peakShake, speedFraction);

            elapsed += Time.deltaTime;
            yield return null;
        }
    }

    // -------------------------------------------------------------------------
    // Editor helpers
    // -------------------------------------------------------------------------

    // Called by Unity when the component is first added or Reset is chosen in the Inspector.
    private void Reset()
    {
        speedCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        var bus = transform.Find("Bus");
        if (bus == null) return;

        busVisual      = bus.gameObject;
        busAudioSource = bus.GetComponent<AudioSource>();
        machineShake   = bus.GetComponent<MachineShake>();
    }
}
