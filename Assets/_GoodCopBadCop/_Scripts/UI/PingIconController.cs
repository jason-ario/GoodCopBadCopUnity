using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Measures and displays network round-trip latency for the local client.
///
/// Because the project's FacepunchTransport returns 0 for GetCurrentRtt, this
/// controller performs its own RTT measurement using NGO's CustomMessagingManager:
///   - The client sends a named "Ping_Request" message to the server.
///   - The server (or host) echoes back a "Ping_Response" immediately.
///   - The client measures the elapsed real time as round-trip latency.
///
/// Ping tiers and thresholds (configurable in the Inspector):
///   Tier 0  Excellent  &lt; 50 ms   — sprite index 0 (green)
///   Tier 1  Good       50–99 ms  — sprite index 1 (yellow-green)
///   Tier 2  Fair       100–149 ms — sprite index 2 (orange)
///   Tier 3  Poor       ≥ 150 ms  — sprite index 3 (red)
///
/// If pingSprites is left empty or under-populated the icon falls back to colour
/// tinting only, so the component works with a single sprite asset.
///
/// Host machines hide the ping display (RTT to self is 0 ms and not meaningful).
/// The request-handler on the host is still registered so it can echo responses
/// back to any connected client that has its own PingIconController active.
/// </summary>
public class PingIconController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────────────────
    // Message names
    // ─────────────────────────────────────────────────────────────────────────

    private const string MsgPingRequest  = "Ping_Request";
    private const string MsgPingResponse = "Ping_Response";

    /// <summary>
    /// How many seconds to wait before resending a ping that received no reply.
    /// Guards against a dropped packet locking up the ping cycle permanently.
    /// </summary>
    private const float TimeoutSec = 3f;

    // ─────────────────────────────────────────────────────────────────────────
    // Inspector fields
    // ─────────────────────────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("Image component used for the signal/ping icon.")]
    [SerializeField] private Image pingIcon;

    [Tooltip("TMP label that shows the current ping in milliseconds.")]
    [SerializeField] private TextMeshProUGUI pingText;

    [Header("Sprites  (Excellent / Good / Fair / Poor)")]
    [Tooltip(
        "Up to 4 sprites in ascending latency order.\n" +
        "Leave empty to use colour tinting only.\n" +
        "Partial arrays are supported — tiers without a sprite keep the previous one.")]
    [SerializeField] private Sprite[] pingSprites;

    [Header("Timing")]
    [Tooltip("Seconds between successive ping measurements.")]
    [SerializeField] private float updateInterval = 1.5f;

    [Header("Thresholds (ms)")]
    [SerializeField] private int excellentMaxMs = 50;
    [SerializeField] private int goodMaxMs      = 100;
    [SerializeField] private int fairMaxMs      = 150;

    // ─────────────────────────────────────────────────────────────────────────
    // Colours per tier
    // ─────────────────────────────────────────────────────────────────────────

    private static readonly Color ColExcellent = new(0.18f, 0.72f, 0.18f, 1f); // green
    private static readonly Color ColGood      = new(0.72f, 0.84f, 0.18f, 1f); // yellow-green
    private static readonly Color ColFair      = new(0.90f, 0.55f, 0.10f, 1f); // orange
    private static readonly Color ColPoor      = new(0.85f, 0.15f, 0.15f, 1f); // red

    // ─────────────────────────────────────────────────────────────────────────
    // Runtime state
    // ─────────────────────────────────────────────────────────────────────────

    private bool  _handlersRegistered;
    private bool  _waitingForPong;
    private float _pingSentTime;
    private float _nextPingTime;

    // ─────────────────────────────────────────────────────────────────────────
    // Unity lifecycle
    // ─────────────────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        _waitingForPong = false;
        _nextPingTime   = Time.time; // send first ping as soon as the network is ready
    }

    private void OnDisable()
    {
        TryUnregisterHandlers();
    }

    private void Update()
    {
        var nm = NetworkManager.Singleton;

        // ── Network not yet active ───────────────────────────────────────────
        if (nm == null || !nm.IsListening)
        {
            TryUnregisterHandlers();
            SetIconsVisible(false);
            return;
        }

        // ── Register handlers once NGO is ready ─────────────────────────────
        if (!_handlersRegistered)
            TryRegisterHandlers(nm);

        // ── Host: show 0 ms (they ARE the server — no network hop) ───────────
        if (nm.IsHost)
        {
            SetIconsVisible(true);
            ApplyPingDisplay(0);
            return;
        }

        // ── Not yet connected as a proper client ─────────────────────────────
        if (!nm.IsConnectedClient)
        {
            SetIconsVisible(false);
            return;
        }

        // ── Client: show display and drive ping cycle ─────────────────────────
        SetIconsVisible(true);

        bool timedOut = _waitingForPong && (Time.time - _pingSentTime) >= TimeoutSec;
        bool intervalElapsed = !_waitingForPong && Time.time >= _nextPingTime;

        if (timedOut || intervalElapsed)
            SendPing(nm);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Handler registration
    // ─────────────────────────────────────────────────────────────────────────

    private void TryRegisterHandlers(NetworkManager nm)
    {
        if (nm.CustomMessagingManager == null) return;

        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgPingRequest,  HandlePingRequest);
        nm.CustomMessagingManager.RegisterNamedMessageHandler(MsgPingResponse, HandlePingResponse);
        _handlersRegistered = true;
    }

    private void TryUnregisterHandlers()
    {
        if (!_handlersRegistered) return;

        var nm = NetworkManager.Singleton;
        if (nm?.CustomMessagingManager != null)
        {
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgPingRequest);
            nm.CustomMessagingManager.UnregisterNamedMessageHandler(MsgPingResponse);
        }

        _handlersRegistered = false;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Ping send / receive
    // ─────────────────────────────────────────────────────────────────────────

    private void SendPing(NetworkManager nm)
    {
        using var writer = new FastBufferWriter(0, Allocator.Temp);
        nm.CustomMessagingManager.SendNamedMessage(
            MsgPingRequest, NetworkManager.ServerClientId, writer);

        _pingSentTime   = Time.time;
        _waitingForPong = true;
        _nextPingTime   = Time.time + updateInterval;
    }

    /// <summary>
    /// Called on the HOST when a client's ping request arrives.
    /// Immediately sends an empty echo back to that client.
    /// </summary>
    private void HandlePingRequest(ulong senderId, FastBufferReader _)
    {
        var nm = NetworkManager.Singleton;
        if (nm?.CustomMessagingManager == null) return;

        using var writer = new FastBufferWriter(0, Allocator.Temp);
        nm.CustomMessagingManager.SendNamedMessage(MsgPingResponse, senderId, writer);
    }

    /// <summary>
    /// Called on the CLIENT when the host echoes the ping back.
    /// Measures elapsed real time and refreshes the UI.
    /// </summary>
    private void HandlePingResponse(ulong _, FastBufferReader __)
    {
        float rttSec = Time.time - _pingSentTime;
        int   pingMs = Mathf.RoundToInt(rttSec * 1000f);

        _waitingForPong = false;
        ApplyPingDisplay(pingMs);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // UI update
    // ─────────────────────────────────────────────────────────────────────────

    private void ApplyPingDisplay(int pingMs)
    {
        if (pingText != null)
            pingText.text = $"{pingMs} ms";

        if (pingIcon == null) return;

        int tier = GetTier(pingMs);

        // Apply colour tint first (always works, even with one sprite)
        pingIcon.color = TierToColor(tier);

        // Swap sprite only when a valid one is configured for this tier
        if (pingSprites != null && pingSprites.Length > tier && pingSprites[tier] != null)
            pingIcon.sprite = pingSprites[tier];
    }

    private int GetTier(int pingMs)
    {
        if (pingMs < excellentMaxMs) return 0;
        if (pingMs < goodMaxMs)      return 1;
        if (pingMs < fairMaxMs)      return 2;
        return 3;
    }

    private static Color TierToColor(int tier) => tier switch
    {
        0 => ColExcellent,
        1 => ColGood,
        2 => ColFair,
        _ => ColPoor
    };

    // ─────────────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────────────

    private void SetIconsVisible(bool visible)
    {
        if (pingIcon != null) pingIcon.enabled = visible;
        if (pingText != null) pingText.enabled = visible;
    }
}
