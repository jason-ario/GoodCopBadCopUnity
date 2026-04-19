using System;
using System.Collections;
using HighlightPlus;
using UnityEngine;
using UnityEngine.Animations;

public class ObjectPlacer : MonoBehaviour
{
    public static ObjectPlacer Instance;
    [SerializeField] private Transform container;
    [SerializeField] private ObjectContainer objectContainer;
    [SerializeField] private LineRenderer arcLine;
    [SerializeField] private int arcSegments = 20;
    [SerializeField] private float arcHeight = 1f;

    private PickableItemData _pickableItemData;
    private PickableObject _clonedItem;
    private PlacementBoard _currentPlacementBoard;
    public bool IsActive;
    public bool deactivatedThisFrame = false;
    public PlacementBoard PlacementBoard => _currentPlacementBoard;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        container.gameObject.SetActive(false);
    }
    
    private void Update()
    {
        if (deactivatedThisFrame)
        {
            deactivatedThisFrame = false;
        }

        if (IsActive)
        {
            RenderArcLineFromPlayerToThis();
        }
    }
    
    void RenderArcLineFromPlayerToThis()
    {
        if (arcLine == null) return;

        PlayerPickupController playerPickup = PlayerInstance.Instance.GetComponent<PlayerPickupController>(); // Assuming you have a way to get the local player
        if (playerPickup == null) return;

        Vector3 startPos = playerPickup.HeldObject.transform.position; // Or a specific hand Transform
        Vector3 endPos = transform.position;
        
        // Control point for the arc (middle point + height)
        Vector3 midPoint = Vector3.Lerp(startPos, endPos, 0.5f);
        midPoint.y += arcHeight + Mathf.Abs(startPos.y - endPos.y) * 0.5f;

        arcLine.positionCount = arcSegments;
        for (int i = 0; i < arcSegments; i++)
        {
            float t = i / (float)(arcSegments - 1);
            // Quadratic Bezier formula
            Vector3 point = Vector3.Lerp(Vector3.Lerp(startPos, midPoint, t), Vector3.Lerp(midPoint, endPos, t), t);
            arcLine.SetPosition(i, point);
        }
    }

    IEnumerator DeactivatedThisFrame()
    {
        deactivatedThisFrame = true;
        yield return new WaitForEndOfFrame();
        deactivatedThisFrame = false;
        _currentPlacementBoard = null;
    }

    public void SetItem(PickableItemData itemData)
    {
        _pickableItemData = itemData;
    }

    // Returns the child slot transform from the objectContainer that matches the item,
    // used as the reference position/rotation for the clone.
    private Transform GetSlotTransform(PickableItemData itemData)
    {
        foreach (var item in objectContainer.ItemsHeld)
        {
            if (item.ItemData == itemData)
                return item.transform;
        }
        return container;
    }

    private void SpawnClone()
    {
        if (_clonedItem != null)
        {
            Destroy(_clonedItem.gameObject);
            _clonedItem = null;
        }

        PlayerPickupController playerPickup = PlayerInstance.Instance.GetComponent<PlayerPickupController>();
        if (playerPickup == null) return;

        PickableObject sourceItem = playerPickup.HeldObject;
        if (sourceItem == null) return;

        // Find the matching slot child in objectContainer by ItemData
        Transform slotTransform = null;
        foreach (var item in objectContainer.ItemsHeld)
        {
            if (item.ItemData == _pickableItemData)
            {
                slotTransform = item.transform;
                break;
            }
        }
        if (slotTransform == null) return;

        // Spawn clone parented to the container, using the slot's local position/rotation
        _clonedItem = Instantiate(sourceItem, container);
        _clonedItem.transform.localPosition = slotTransform.localPosition;
        _clonedItem.transform.localRotation = slotTransform.localRotation;
        _clonedItem.GetComponent<PickableObject>().SetPlacementClone();

        // Jump clone's animator to the exact state of the source
        Animator sourceAnimator = sourceItem.GetComponentInChildren<Animator>();
        Animator cloneAnimator = _clonedItem.GetComponentInChildren<Animator>();
        if (sourceAnimator != null && cloneAnimator != null)
        {
            // Copy all parameter values first
            foreach (AnimatorControllerParameter param in sourceAnimator.parameters)
            {
                switch (param.type)
                {
                    case AnimatorControllerParameterType.Float:
                        cloneAnimator.SetFloat(param.nameHash, sourceAnimator.GetFloat(param.nameHash));
                        break;
                    case AnimatorControllerParameterType.Int:
                        cloneAnimator.SetInteger(param.nameHash, sourceAnimator.GetInteger(param.nameHash));
                        break;
                    case AnimatorControllerParameterType.Bool:
                        cloneAnimator.SetBool(param.nameHash, sourceAnimator.GetBool(param.nameHash));
                        break;
                }
            }

            // Then jump to the exact state on each layer
            for (int i = 0; i < sourceAnimator.layerCount; i++)
            {
                AnimatorStateInfo stateInfo = sourceAnimator.GetCurrentAnimatorStateInfo(i);
                cloneAnimator.Play(stateInfo.fullPathHash, i, stateInfo.normalizedTime);
            }

            // Force immediate pose evaluation
            cloneAnimator.enabled = false;
            cloneAnimator.enabled = true;
        }
        
        // Disable the ParentConstraint so it doesn't override the position we just set
        ParentConstraint parentConstraint = _clonedItem.GetComponent<ParentConstraint>();
        if (parentConstraint != null)
            parentConstraint.constraintActive = false;
        
    }

    public void ActivatePlacer(PlacementBoard placementBoard)
    {
        _currentPlacementBoard = placementBoard;
        container.gameObject.SetActive(true);
        IsActive = true;

        SpawnClone();

        if (_clonedItem != null)
            _clonedItem.gameObject.SetActive(true);

        if (arcLine != null) arcLine.enabled = true;
    }

    public void DeactivatePlacer()
    {
        if (_clonedItem != null)
        {
            Destroy(_clonedItem.gameObject);
            _clonedItem = null;
        }

        container.gameObject.SetActive(false);
        IsActive = false;

        if (arcLine != null)
        {
            arcLine.positionCount = 0;
            arcLine.enabled = false;
        }
        StartCoroutine(DeactivatedThisFrame());
    }

    public GameObject GetPickableObject(PickableItemData heldObject)
    {
        foreach (var item in objectContainer.ItemsHeld)
        {
            if (item.ItemData == heldObject) // _pickableItemData is null if SetItem() wasn't called!
            {
                return item.gameObject;
                break;
            }
        }
        
        return null;
    }
}
