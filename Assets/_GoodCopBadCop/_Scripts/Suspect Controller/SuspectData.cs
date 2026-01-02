using UnityEngine;

[CreateAssetMenu(fileName = "Suspect Data", menuName = "ScriptableObjects/Suspect Data", order = 1)]
public class SuspectData : ScriptableObject
{
   public SuspectCharacter suspectPrefab;

   [TextArea(3, 10)]
   public string entryDialogue;
   public AudioClip[] voiceAudioClips;
}
