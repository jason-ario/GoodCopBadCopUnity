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
    [Min(0)] public int dailyInfectionProgression = 5;

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
    public DialogueByVerdict exitDialoguesPassed = 
        new DialogueByVerdict(Verdict.Passed, new string[3], new string[3], new string[3]);    
    public DialogueByVerdict exitDialoguesQuarantined = 
        new DialogueByVerdict(Verdict.Quarantined, new string[3], new string[3], new string[3]);    
    public DialogueByVerdict exitDialoguesKilled = 
        new DialogueByVerdict(Verdict.Killed, new string[3], new string[3], new string[3]);    

    // Questions:
    // Have you been experiencing any strange symptoms lately?
    // Where are you coming from?
    // Who do live with?
    
    /*[System.Serializable]
    public struct QuestionDialogueSet
    {
        public string[] earlyDaysAnswers;   // Days 1-10
        public string[] midDaysAnswers;     // Days 11-20
        public string[] finalDaysAnswers;   // Days 21-30
    }*/

    /*public QuestionDialogueSet whereAreYouComingFromAnswers;
    public QuestionDialogueSet haveYouBeenExperiencingAnySymptomsAnswers;
    public QuestionDialogueSet whoDoYouLiveWithAnswers;*/
    
    public AudioClip[] voiceAudioClips;
    [System.Serializable]
    public struct Response
    {
        [TextArea(3, 10)]
        public string text;
    }

    public string IDNumber;

    [ContextMenu("Set Random ID Number")]
    public void SetRandomIDNumber()
    {
        IDNumber = Random.Range(1000000, 9999999).ToString();
    }
    
}