using System.Collections.Generic;
using UnityEngine;

public class DailySuspectManager : MonoBehaviour
{
    private List<SuspectSet> currentDayLineup = new();
    public List<SuspectSet> CurrentDayLineup => currentDayLineup;
}