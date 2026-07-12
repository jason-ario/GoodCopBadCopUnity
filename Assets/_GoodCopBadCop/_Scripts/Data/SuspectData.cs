using UnityEngine;
using UnityEngine.Serialization;

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
    public Vector2Int dailyInfectionProgression = new Vector2Int(3, 8);

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

    [System.Serializable]
    public struct EntryReasonSet
    {
        public string[] earlyDaysReasons;   // Days 1-10
        public string[] midDaysReasons;     // Days 11-20
        public string[] finalDaysReasons;   // Days 21-30
    }
    
    public EntryReasonSet entryReasons;
    public EntryReasonSet invalidEntryReasons;

    public enum Verdict
    {
        None = 0,
        Passed = 1,
        Quarantined = 2,
        Killed = 3,
    }
    
    [System.Serializable]
    public struct DialogueByVerdict
    {
        public Verdict lastVerdict;
        [FormerlySerializedAs("entryDialoguesEarlyDays")] public string[] dialoguesEarlyDays;
        [FormerlySerializedAs("entryDialoguesMidDays")] public string[] dialoguesMidDays;
        [FormerlySerializedAs("entryDialoguesFinalDays")] public string[] dialoguesFinalDays;
    
        public DialogueByVerdict(Verdict verdict, string[] early, string[] mid, string[] final)
        {
            lastVerdict = verdict;
            dialoguesEarlyDays = early;
            dialoguesMidDays = mid;
            dialoguesFinalDays = final;
        }
    }

    public DialogueByVerdict entryDialogues = 
        new DialogueByVerdict(Verdict.None, new string[3], new string[3], new string[3]);
    [Tooltip("Entry dialogues used at infection stage 3+. Falls back to entryDialogues if empty.")]
    public DialogueByVerdict uncannyEntryDialogues =
        new DialogueByVerdict(Verdict.None, new string[3], new string[3], new string[3]);
    public DialogueByVerdict exitDialoguesPassed = 
        new DialogueByVerdict(Verdict.Passed, new string[3], new string[3], new string[3]);    
    public DialogueByVerdict exitDialoguesQuarantined = 
        new DialogueByVerdict(Verdict.Quarantined, new string[3], new string[3], new string[3]);    
    public DialogueByVerdict exitDialoguesKilled = 
        new DialogueByVerdict(Verdict.Killed, new string[3], new string[3], new string[3]);    

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
        [TextArea(2, 6)] public string earlyDaysAnswer;
        [TextArea(2, 6)] public string midDaysAnswer;
        [TextArea(2, 6)] public string finalDaysAnswer;

        [Header("Story Mismatch — served instead of normal answer when StoryMismatchAnomaly is active")]
        [TextArea(2, 6)] public string mismatchEarlyDaysAnswer;
        [TextArea(2, 6)] public string mismatchMidDaysAnswer;
        [TextArea(2, 6)] public string mismatchFinalDaysAnswer;

        [Header("Uncanny Arc — served instead of normal answer at infection stage 3+")]
        [TextArea(2, 6)] public string uncannyEarlyDaysAnswer;
        [TextArea(2, 6)] public string uncannyMidDaysAnswer;
        [TextArea(2, 6)] public string uncannyFinalDaysAnswer;
    }

    public QuestionResponseSet[] questionResponses;

    [System.Serializable]
    public struct BarkSet
    {
        [TextArea(1, 3)] public string[] earlyDays;
        [TextArea(1, 3)] public string[] midDays;
        [TextArea(1, 3)] public string[] finalDays;

        [Header("Uncanny Arc — used at infection stage 3+")]
        [TextArea(1, 3)] public string[] uncannyEarlyDays;
        [TextArea(1, 3)] public string[] uncannyMidDays;
        [TextArea(1, 3)] public string[] uncannyFinalDays;
    }

    [Header("Idle Barks — Random ambient lines while at the booth")]
    public BarkSet idleBarks;

    public string IDNumber;

    [ContextMenu("Set Random ID Number")]
    public void SetRandomIDNumber()
    {
        IDNumber = Random.Range(1000000, 9999999).ToString();
    }

}