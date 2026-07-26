using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked intro cutscene for the bus. On spawn the bus idles with ambient audio,
/// then after <see cref="idleDuration"/> seconds it crossfades to the driving SFX,
/// accelerates off-screen along <see cref="driveLocalDirection"/> and despawns —
/// synced for all clients via a ClientRpc.
/// Requires a NetworkObject component on this GameObject.
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

    public override void OnNetworkSpawn()
    {
        // Keep the bus hidden and silent until the first lobby player has actually
        // spawned in, so it doesn't idle on screen before anyone arrives.
        if (busVisual != null)
            busVisual.SetActive(false);

        // Server waits for the first lobby player spawn before revealing the bus
        // and starting the sequence.
        if (IsServer)
            PlayerSpawner.OnPlayerSpawnedAtLobby += OnFirstPlayerSpawnedAtLobby;
    }

    public override void OnNetworkDespawn()
    {
        PlayerSpawner.OnPlayerSpawnedAtLobby -= OnFirstPlayerSpawnedAtLobby;
    }

    private void OnFirstPlayerSpawnedAtLobby(ulong clientId)
    {
        // Unsubscribe immediately so the sequence only starts once.
        PlayerSpawner.OnPlayerSpawnedAtLobby -= OnFirstPlayerSpawnedAtLobby;

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
    /// then despawns after the full drive duration has elapsed.
    /// </summary>
    private IEnumerator ServerSequence()
    {
        yield return new WaitForSeconds(idleDuration);

        BeginDriveClientRpc();

        // Account for the rev delay + full movement duration, then add a small buffer
        // so clients finish their coroutines cleanly before the NetworkObject is destroyed.
        yield return new WaitForSeconds(driveRevDelay + driveDuration + 0.5f);

        if (NetworkObject != null && NetworkObject.IsSpawned)
            NetworkObject.Despawn();
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
