using System;
using System.Collections.Generic;
using UnityEngine;
using Mathf = UnityEngine.Mathf;
using System.Linq;

public class SuspectDatabase : MonoBehaviour
{
    public static SuspectDatabase Instance;

    [SerializeField] private SuspectSet allSuspects;
}