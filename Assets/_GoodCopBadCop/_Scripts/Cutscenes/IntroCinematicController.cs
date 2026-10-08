using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Plays a short, skippable intro story sequence — plain white text centered over a black
/// screen — the first time the game is started in this application session. Runs while the
/// screen fader is already fully black, before the screen unfades. The server spawns players
/// just before this starts, so the local player's controls are locked for its duration.
///
/// This is purely local/client-side: each connected player reveals and advances through the
/// lines independently — and reveals into gameplay as soon as they finish, never waiting on
/// another player — by pressing E, clicking, or a gamepad button, mirroring the skip/advance
/// convention used elsewhere in the dialogue system — the first input completes the typewriter
/// reveal for that line, a further input advances to the next line.
///
/// Call <see cref="PlayIfNeeded"/> from <see cref="GameManager.LobbyTransitionSequence"/>.
/// Subsequent calls (e.g. after a Restart Day scene reload) are no-ops once the cinematic has
/// played once for this application run — <see cref="_hasPlayed"/> is static so it survives
/// the scene reload within the same AppDomain, exactly like <c>GameManager._isRestartingDay</c>.
/// </summary>
public class IntroCinematicController : MonoBehaviour
{
    public static IntroCinematicController Instance { get; private set; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMPTextReveal textReveal;
    [SerializeField] private CanvasGroup continuePrompt;

    [Header("Continue Prompt Animation")]
    [SerializeField] private float promptDelay = 0.5f;
    [SerializeField] private float promptFadeInDuration = 0.6f;
    [SerializeField] private float promptFadeOutDuration = 0.2f;

    [Tooltip("Story pages shown in order, one at a time, over the black screen. Put each sentence on its own line.")]
    [TextArea(2, 6)]
    [SerializeField]
    private string[] storyLines =
    {
        "20 October 1989 - Saplavi Checkpoint, Soviet Georgia",
        "The disaster began beyond the northern mountains.",
        "No one knows exactly what happened.\nThe government called it an industrial accident.\nThen the radiation reached the villages, and people stopped acting like themselves.",
        "Saplavi is one of the last towns still standing.",
        "Its checkpoint is the only barrier between the valley and the outside world.\nThe healthy may pass.\nThe infected must be quarantined.",
        "Anything no longer human must be eliminated.",
        "You have been assigned to the checkpoint.",
        "Inspect everyone.\nFollow protocol.\nProtect the town.",
        "And remember, decisions have consequences..."
    };

    /// <summary>True once the intro cinematic has played for this application session.</summary>
    private bool _hasPlayed;

    private bool _awaitingInput;
    private bool _advanceRequested;

    /// <summary>True while the intro cinematic panel is showing on this client.</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>
    /// True once this cinematic has locked the local player's controls. The server spawns
    /// players before the cinematic starts (so no player has to wait for another to finish
    /// reading), which means the local player object can appear mid-cinematic — its input must
    /// stay locked until this player finishes, or E/click presses would leak into gameplay.
    /// </summary>
    private PlayerInstance _lockedPlayer;

    private void Awake()
    {
        Instance = this;

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void Update()
    {
        // Re-assert every frame, not once: the host's player spawns (and its spawn restore grants
        // control) just before the cinematic starts, and a respawned player object replaces the
        // locked one. The CanControl/SetCanLook guards block re-enables while IsPlaying.
        PlayerInstance local = PlayerInstance.Instance;
        if (IsPlaying && local != null && (local != _lockedPlayer || local.CanControl))
        {
            _lockedPlayer = local;
            _lockedPlayer.OpenedUIPanel();
        }

        if (!_awaitingInput || _advanceRequested) return;

        bool overUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        bool pressed = Input.GetKeyDown(KeyCode.E)
                       || (Input.GetMouseButtonDown(0) && !overUI)
                       || (Gamepad.current?.buttonSouth.wasPressedThisFrame ?? false)
                       || (Gamepad.current?.startButton.wasPressedThisFrame ?? false);

        if (!pressed) return;

        if (textReveal != null && textReveal.IsRevealing)
        {
            // First input just completes the typewriter for this line — does not advance yet.
            textReveal.CompleteReveal();
            return;
        }

        _advanceRequested = true;
    }

    /// <summary>
    /// Plays the intro cinematic once per application session, and only on Day 1 — loading a
    /// save from Day 2 onwards (or being on any later day when this first runs) must never show
    /// it. Safe to call every time the lobby transition runs (e.g. Restart Day) — it is a no-op
    /// after the first successful play. Runs entirely locally; call this on every client (it
    /// does not need network sync).
    /// </summary>
    public IEnumerator PlayIfNeeded()
    {
        if (_hasPlayed || panelRoot == null || textReveal == null || storyLines == null || storyLines.Length == 0)
            yield break;

        if (CampaignManager.Instance != null && CampaignManager.Instance.CurrentDay > 1)
        {
            _hasPlayed = true;
            yield break;
        }

        _hasPlayed = true;

        panelRoot.SetActive(true);
        IsPlaying = true;

        // Lock immediately (same frame) rather than waiting for Update.
        if (PlayerInstance.Instance != null)
        {
            _lockedPlayer = PlayerInstance.Instance;
            _lockedPlayer.OpenedUIPanel();
        }

        foreach (string line in storyLines)
            yield return StartCoroutine(ShowLineAndWait(line));

        panelRoot.SetActive(false);
        IsPlaying = false;

        if (_lockedPlayer != null)
        {
            _lockedPlayer.ClosedUIPanel();
            _lockedPlayer = null;
        }
    }

    private IEnumerator ShowLineAndWait(string line)
    {
        if (continuePrompt != null)
            continuePrompt.alpha = 0f;

        _advanceRequested = false;
        _awaitingInput = true;

        textReveal.RevealText(line);

        yield return new WaitUntil(() => !textReveal.IsRevealing);

        // Let the line breathe before offering to advance, then fade the prompt in.
        float t = 0f;
        while (!_advanceRequested)
        {
            t += Time.unscaledDeltaTime;
            if (continuePrompt != null)
            {
                float fadeIn = Mathf.Clamp01((t - promptDelay) / promptFadeInDuration);
                continuePrompt.alpha = t < promptDelay ? 0f : fadeIn;
            }
            yield return null;
        }

        _awaitingInput = false;

        // Brief flash-and-fade acknowledgement of the press.
        if (continuePrompt != null)
        {
            float f = 0f;
            while (f < promptFadeOutDuration)
            {
                f += Time.unscaledDeltaTime;
                continuePrompt.alpha = 1f - Mathf.Clamp01(f / promptFadeOutDuration);
                yield return null;
            }
            continuePrompt.alpha = 0f;
        }
    }
}
