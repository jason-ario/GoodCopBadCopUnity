using System.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Schedules random ambient barks for a suspect while they wait at the booth.
/// Only runs logic on the server; <see cref="SpeakingInteraction.Say"/> handles
/// replication to all clients automatically.
///
/// Barks are suppressed when dialogue is actively playing or after a verdict has been given.
/// A minimum cooldown of <see cref="EntryCooldownSeconds"/> is enforced from the moment
/// <see cref="BeginBarkSchedule"/> is called (i.e. right after entry dialogue fires).
/// </summary>
[RequireComponent(typeof(SuspectCharacter))]
public class SuspectBarkController : NetworkBehaviour
{
    private const float EntryCooldownSeconds = 20f;
    private static readonly Vector2 BarkIntervalRange = new Vector2(20f, 60f);

    private SuspectCharacter _suspect;
    private Coroutine _barkCoroutine;
    private bool _verdictGiven;

    private void Awake()
    {
        _suspect = GetComponent<SuspectCharacter>();
    }

    /// <summary>
    /// Starts the bark schedule. Must only be called on the server.
    /// Enforces a <see cref="EntryCooldownSeconds"/> delay before the first bark can fire.
    /// </summary>
    public void BeginBarkSchedule()
    {
        if (!IsServer) return;

        _verdictGiven = false;

        if (_barkCoroutine != null)
            StopCoroutine(_barkCoroutine);

        _barkCoroutine = StartCoroutine(BarkRoutine());
    }

    /// <summary>
    /// Permanently stops all pending barks. Call this when a verdict is delivered.
    /// Must only be called on the server.
    /// </summary>
    public void StopBarks()
    {
        if (!IsServer) return;

        _verdictGiven = true;

        if (_barkCoroutine != null)
        {
            StopCoroutine(_barkCoroutine);
            _barkCoroutine = null;
        }
    }

    private IEnumerator BarkRoutine()
    {
        yield return new WaitForSeconds(EntryCooldownSeconds);

        while (!_verdictGiven)
        {
            float delay = Random.Range(BarkIntervalRange.x, BarkIntervalRange.y);
            yield return new WaitForSeconds(delay);

            if (_verdictGiven) yield break;
            if (DialogueManager.Instance == null) continue;
            if (DialogueManager.Instance.IsSpeaking) continue;

            string bark = ResolveBark();
            if (!string.IsNullOrEmpty(bark) && _suspect.Speaking != null)
                _suspect.Speaking.Say(bark);
        }
    }

    private string ResolveBark()
    {
        if (_suspect == null || _suspect.Data == null) return null;

        string[] lines;

        // Replacements always use the uncanny bark pool when authored.
        if (_suspect.IsReplacement)
        {
            if (ShiftManager.Instance.IsEarlyDays)
                lines = _suspect.Data.idleBarks.uncannyEarlyDays;
            else if (ShiftManager.Instance.IsMidDays)
                lines = _suspect.Data.idleBarks.uncannyMidDays;
            else
                lines = _suspect.Data.idleBarks.uncannyFinalDays;

            // Fall back to normal barks if uncanny pool is empty.
            if (lines == null || lines.Length == 0)
            {
                if (ShiftManager.Instance.IsEarlyDays)
                    lines = _suspect.Data.idleBarks.earlyDays;
                else if (ShiftManager.Instance.IsMidDays)
                    lines = _suspect.Data.idleBarks.midDays;
                else
                    lines = _suspect.Data.idleBarks.finalDays;
            }
        }
        else
        {
            if (ShiftManager.Instance.IsEarlyDays)
                lines = _suspect.Data.idleBarks.earlyDays;
            else if (ShiftManager.Instance.IsMidDays)
                lines = _suspect.Data.idleBarks.midDays;
            else
                lines = _suspect.Data.idleBarks.finalDays;
        }

        if (lines == null || lines.Length == 0) return null;

        return lines[Random.Range(0, lines.Length)];
    }
}
