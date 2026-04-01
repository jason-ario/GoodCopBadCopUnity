using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "New Suspect Set", menuName = "ScriptableObjects/Suspect Set", order = 1)]
public class SuspectSet : ScriptableObject
{
    public List<SuspectData> suspects;
}
