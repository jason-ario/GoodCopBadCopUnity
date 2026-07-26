using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

/// <summary>
/// Plays a short, skippable intro story sequence — plain white text centered over a black
/// screen — the first time the game is started in this application session. Runs while the
/// screen fader is already fully black, before the player spawns in and the screen unfades.
///
/// This is purely local/client-side: each connected player reveals and advances through the
/// lines independently by pressing E, clicking, or a gamepad button, mirroring the skip/advance
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

    [Tooltip("Story lines shown in order, one at a time, over the black screen.")]
    [TextArea(2, 4)]
    [SerializeField]
    private string[] storyLines =
    {
        "The disaster began beyond the northern mountains.",
        "No one knows exactly what happened. The government called it an industrial accident. Then the radiation reached the villages, and people stopped acting like themselves.",
        "Saplavi is one of the last towns still standing.",
        "Its checkpoint is the only barrier between the valley and the outside world. The healthy may pass. The infected must be quarantined.",
        "Anything no longer human must be eliminated.",
        "You have been assigned to the checkpoint.",
        "Inspect everyone. Follow protocol. Protect the town.",
        "And remember\u2014the mutations are learning how to hide."
    };

    /// <summary>True once the intro cinematic has played for this application session.</summary>
    private bool _hasPlayed;

    private bool _awaitingInput;
    private bool _advanceRequested;

    private void Awake()
    {
        Instance = this;

        if (panelRoot != null)
            panelRoot.SetActive(false);
    }

    private void Update()
    {
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
    /// Plays the intro cinematic once per application session. Safe to call every time the
    /// lobby transition runs (e.g. Restart Day) — it is a no-op after the first successful play.
    /// Runs entirely locally; call this on every client (it does not need network sync).
    /// </summary>
    public IEnumerator PlayIfNeeded()
    {
        if (_hasPlayed || panelRoot == null || textReveal == null || storyLines == null || storyLines.Length == 0)
            yield break;

        _hasPlayed = true;

        panelRoot.SetActive(true);

        foreach (string line in storyLines)
            yield return StartCoroutine(ShowLineAndWait(line));

        panelRoot.SetActive(false);
    }

    private IEnumerator ShowLineAndWait(string line)
    {
        if (continuePrompt != null)
            continuePrompt.alpha = 0f;

        _advanceRequested = false;
        _awaitingInput = true;

        textReveal.RevealText(line);

        yield return new WaitUntil(() => !textReveal.IsRevealing);

        if (continuePrompt != null)
            continuePrompt.alpha = 1f;

        yield return new WaitUntil(() => _advanceRequested);

        _awaitingInput = false;
    }
}
