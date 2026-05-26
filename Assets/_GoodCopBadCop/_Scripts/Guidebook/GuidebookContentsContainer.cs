using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Singleton on the Guide Book Contents Container scene object.
/// Exposes Open/Close to toggle the contents child, which houses all
/// guidebook render cameras. The root object stays permanently active
/// so the singleton is always reachable at runtime.
/// </summary>
public class GuidebookContentsContainer : MonoBehaviour
{

    [SerializeField] private GameObject _contents;

    IEnumerator Start()
    {
        _contents.SetActive(true);
        yield return new WaitForEndOfFrame();
        _contents.SetActive(false);
    }
}
