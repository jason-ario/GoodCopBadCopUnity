using UnityEngine;

[CreateAssetMenu(fileName = "Suspect Data", menuName = "ScriptableObjects/Suspect Data", order = 1)]
public class SuspectData : ScriptableObject
{
   public string name;
   public int age;
   public string occupation;
   public GameObject suspectPrefab;
   [TextArea( 10, 10)]
   public string personality;
   [TextArea( 10, 10)]
   public string conversationStyle;
   [TextArea( 10, 10)]
   public string weakness;
   public Texture2D portrait;
   public bool isGuilty;
}
