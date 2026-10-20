using UnityEngine;

[CreateAssetMenu(menuName = "Scriptable Objects/Suspect Data")]
public class SuspectData : ScriptableObject
{
    [Header("Suspect Data")]
    public string FirstName;
    public string LastName;
    public string Nickname;
    public string Occupation;
    [TextArea(3, 10)]
    public string CharacterDescription;
    [TextArea(3, 10)]
    public string CharacterArc;
    public string DateOfBirth;
    public string Sex;
    public string EntryPermitExpiryDate;
    public bool IsResident = true;
    public bool GivesPaperwork = true;
    public TMPro.TMP_FontAsset handwritingFont;

    [Header("Infection")]
    [Range(0, 100)] public int startingInfectionScore = 0;
    [Tooltip("Min and max infection score added per day (noise range).")]
    public Vector2Int dailyInfectionProgression = new Vector2Int(8, 18);

    [Header("Uncanny Arc — Profile")]
    [Tooltip("Internal alter-ego name — not shown to the player.")]
    public string alterEgoName;
    [Tooltip("3–5 one-liners describing the suspect's normal presentation, shown on the terminal.")]
    public string[] basePersonalityDescriptors;
    [TextArea(3, 6)]
    [Tooltip("Authored note on expected behavior, shown on the terminal.")]
    public string normalBehaviorNotes;

    [Header("Full Mutant State")]
    [Tooltip("Scripted dialogue played when this suspect arrives at the booth window in their fully-mutated form. " +
             "Must be assigned for the full-mutant path to activate — mirrors introDialogue in structure and usage. " +
             "Leave null to disable the full-mutant booth encounter for this suspect.")]
    public ScriptedDialogue fullMutantDialogue;

    [Header("Replacement System")]
    [Tooltip("Face photo used when this suspect returns as an uncanny replacement after being killed. " +
             "Leave null to disable replacement for this character.")]
    public Texture2D replacementIDPhoto;

    public Texture2D IDPhoto;
    public SuspectCharacter CharacterPrefab;

    [Header("Paperwork")]
    [Tooltip("Entry reasons shown on the application form.")]
    public string[] entryReasons;
    [Tooltip("Entry reasons used when InvalidEntryReasonAnomaly is active.")]
    public string[] invalidEntryReasons;

    public enum Verdict
    {
        None = 0,
        Passed = 1,
        Quarantined = 2,
        Killed = 3,
    }

    [Header("Booth Dialogue")]
    public string[] entryDialogues;
    [Tooltip("Entry dialogues used at infection stage 3+. Falls back to entryDialogues if empty.")]
    public string[] uncannyEntryDialogues;
    public string[] exitDialoguesPassed;
    public string[] exitDialoguesQuarantined;
    public string[] exitDialoguesKilled;

    [Header("Exit Lines — Random Pool")]
    [TextArea(1, 3)]
    public string[] quarantineExitLines;
    [TextArea(1, 3)]
    public string[] killExitLines;

    public AudioClip[] voiceAudioClips;

    [Header("First Encounter")]
    [Tooltip("Scripted dialogue played automatically the very first time this suspect arrives at the booth window. " +
             "Leave null for suspects with no special first-meeting cutscene. " +
             "Encounter state is persisted via PlayerPrefs so this plays exactly once across all sessions.")]
    public ScriptedDialogue introDialogue;

    [System.Serializable]
    public struct QuestionResponseSet
    {
        [TextArea(1, 3)] public string question;
        [TextArea(2, 6)] public string answer;

        [Tooltip("Served instead of the normal answer when StoryMismatchAnomaly is active.")]
        [TextArea(2, 6)] public string mismatchAnswer;

        [Tooltip("Served instead of the normal answer at infection stage 3+.")]
        [TextArea(2, 6)] public string uncannyAnswer;

        [Header("Camera")]
        [Tooltip("Optional camera cut while this answer plays. Same keys as ScriptedDialogueNode.cameraTrigger: " +
                 "'SuspectCam', 'SuspectFaceCam' / 'suspect face', or a ScriptedDialogueRunner registry key. " +
                 "Leave empty for the default dialogue camera.")]
        public string cameraTrigger;
    }

    public QuestionResponseSet[] questionResponses;

    [Header("Idle Barks — Random ambient lines while at the booth")]
    [TextArea(1, 3)] public string[] idleBarks;
    [Tooltip("Barks used by uncanny replacements. Falls back to idleBarks if empty.")]
    [TextArea(1, 3)] public string[] uncannyIdleBarks;

    public string IDNumber;

    [ContextMenu("Set Random ID Number")]
    public void SetRandomIDNumber()
    {
        IDNumber = Random.Range(1000000, 9999999).ToString();
    }

}