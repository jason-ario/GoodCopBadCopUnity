using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class PCTerminalEmulatorBootstrapper : MonoBehaviour
{
    [Header("Scene References")]
    [SerializeField] private PC pc;
    [SerializeField] private SuspectRunRecords runRecords;
    [SerializeField] private SuspectSet allSuspects;

    [Header("Seed Data")]
    [SerializeField] private int currentDay = 6;
    [SerializeField] private int killedCount = 2;
    [SerializeField] private int quarantinedCount = 2;

    private bool terminalOpened;

    private IEnumerator Start()
    {
        if (pc == null)
            pc = FindFirstObjectByType<PC>();

        if (runRecords == null)
            runRecords = FindFirstObjectByType<SuspectRunRecords>();

        if (allSuspects == null && runRecords != null)
            allSuspects = runRecords.allSuspects;

        yield return null;

        SeedTerminalState();

        if (pc != null)
        {
            pc.DebugSetCurrentDay(currentDay);
            OpenTerminal();
        }
    }

    private void Update()
    {
        if (pc == null)
            return;

        if (Input.GetKeyDown(KeyCode.E) || Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.Space))
            OpenTerminal();

        if (Input.GetKeyDown(KeyCode.Alpha1)) pc.OpenResidents();
        if (Input.GetKeyDown(KeyCode.Alpha2)) pc.OpenAll();
        if (Input.GetKeyDown(KeyCode.Alpha3)) pc.OpenDeceased();
        if (Input.GetKeyDown(KeyCode.Alpha4)) pc.OpenQuarantine();
        if (Input.GetKeyDown(KeyCode.Alpha5)) pc.OpenNews();
    }

    private void OpenTerminal()
    {
        if (pc == null)
            return;

        pc.DebugOpenTerminal();
        terminalOpened = true;
    }

    private void SeedTerminalState()
    {
        if (runRecords == null || allSuspects == null || allSuspects.suspects == null)
            return;

        List<SuspectData> candidates = allSuspects.suspects
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.FirstName) && !string.IsNullOrWhiteSpace(s.LastName))
            .Distinct()
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();

        foreach (SuspectData suspect in candidates)
        {
            SuspectRecord record = runRecords.GetRecord(suspect);
            if (record == null)
                continue;

            record.isKilled = false;
            record.killedOnDay = -1;
            record.isReplacement = false;
            record.quarantinedOnDay = -1;
            record.pendingVaccineReset = false;
            record.infectionScore = suspect.startingInfectionScore;
        }

        IEnumerable<SuspectData> killedSuspects = candidates.Take(Mathf.Max(0, killedCount));
        foreach (SuspectData suspect in killedSuspects)
        {
            SuspectRecord record = runRecords.GetRecord(suspect);
            if (record == null)
                continue;

            record.isKilled = true;
            record.killedOnDay = Mathf.Max(1, currentDay - 1);
        }

        List<SuspectData> quarantinedSuspects = candidates
            .Skip(Mathf.Max(0, killedCount))
            .Take(Mathf.Max(0, quarantinedCount))
            .ToList();

        for (int i = 0; i < quarantinedSuspects.Count; i++)
        {
            SuspectRecord record = runRecords.GetRecord(quarantinedSuspects[i]);
            if (record == null)
                continue;

            record.quarantinedOnDay = Mathf.Max(1, currentDay - i);
            record.pendingVaccineReset = true;
        }

        Debug.Log($"[PCTerminalEmulator] Seeded {killedCount} deceased and {quarantinedSuspects.Count} quarantined records for Day {currentDay}.");
    }
}
