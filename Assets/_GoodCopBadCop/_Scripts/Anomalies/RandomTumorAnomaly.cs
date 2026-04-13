using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class RandomTumorAnomaly : MutationAnomaly
{
    [SerializeField] private GameObject[] tumors;
    
    private Transform skeletonRoot;
    private Transform[] bones;
    private Vector3[] initialPositions;
    private Quaternion[] initialRotations;
    private Transform[] parentBones;

    private void Initialize()
    {
        skeletonRoot = transform.root.GetComponent<SuspectCharacter>().animator.avatarRoot;
        
        // Get all bones in the character
        bones = skeletonRoot.GetComponentsInChildren<Transform>();
        
        // Store initial positions and rotations, and find closest bone for each tumor
        initialPositions = new Vector3[tumors.Length];
        initialRotations = new Quaternion[tumors.Length];
        parentBones = new Transform[tumors.Length];
        
        for (int i = 0; i < tumors.Length; i++)
        {
            if (tumors[i] != null)
            {
                initialPositions[i] = tumors[i].transform.localPosition;
                initialRotations[i] = tumors[i].transform.localRotation;
                
                // Find the closest bone
                parentBones[i] = FindClosestBone(tumors[i].transform.position);
                
                tumors[i].SetActive(false);
            }
        }
    }

    private Transform FindClosestBone(Vector3 tumorPosition)
    {
        Transform closestBone = null;
        float closestDistance = float.MaxValue;
        
        foreach (var bone in bones)
        {
            // Skip the root itself if you prefer
            if (bone == skeletonRoot)
                continue;
                
            float distance = Vector3.Distance(tumorPosition, bone.position);
            
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestBone = bone;
            }
        }
        
        return closestBone != null ? closestBone : skeletonRoot;
    }

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        Initialize();
        ActivateTumor();
    }

    void ActivateTumor()
    {
        int tumorIndex = Random.Range(0, tumors.Length);
        GameObject selectedTumor = tumors[tumorIndex];
        
        if (selectedTumor != null && parentBones[tumorIndex] != null)
        {
            // Parent the tumor to its closest bone
            selectedTumor.transform.SetParent(parentBones[tumorIndex]);
            
            // Restore the initial position and rotation as local to the bone
            selectedTumor.transform.localPosition = initialPositions[tumorIndex];
            selectedTumor.transform.localRotation = initialRotations[tumorIndex];
            
            selectedTumor.SetActive(true);
        }
    }
}
