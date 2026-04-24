using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SC_Vefects_Easy_Spawn : MonoBehaviour
{

    public GameObject toSpawnVFX;
    public GameObject WhereToSpawn;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space))
        {
            Instantiate(toSpawnVFX, WhereToSpawn.transform);
        }
    }
}
