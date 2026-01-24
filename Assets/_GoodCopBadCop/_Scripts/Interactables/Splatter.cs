using System;
using System.Collections;
using UnityEngine;
using Random = System.Random;

public class Splatter : MonoBehaviour
{
    [SerializeField] private GameObject[] splatters;

    private void OnEnable()
    {
        StartCoroutine(SpawnSplatter());
    }
    
    IEnumerator SpawnSplatter()
    {
        foreach (GameObject splatter in splatters)
        {
            yield return new WaitForSeconds(UnityEngine.Random.Range(.01f, .03f));
            splatter.gameObject.SetActive(true);
        }
    }
}
