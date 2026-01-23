using System;
using System.Collections;
using HighlightPlus;
using UnityEngine;

public class ObjectPlacer : MonoBehaviour
{
    public static ObjectPlacer Instance;
    [SerializeField] private Transform container;
    [SerializeField] private ObjectContainer objectContainer;
    [SerializeField] private LineRenderer arcLine;
    [SerializeField] private int arcSegments = 20;
    [SerializeField] private float arcHeight = 1f;

    private PickableItemData _pickableItemData;
    public bool IsActive;
    public bool deactivatedThisFrame = false;
    
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

        Vector3 startPos = playerPickup.ObjectContainer.CurrentlyEquippedItem.transform.position; // Or a specific hand Transform
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
    }

    public void SetItem(PickableItemData itemData)
    {
        _pickableItemData = itemData;
        
        foreach (var pickableObject in objectContainer.ItemsHeld)
        {
            if (pickableObject.ItemData == itemData)
            {
                pickableObject.gameObject.SetActive(true);
            }
            else
            {
                pickableObject.gameObject.SetActive(false);
            }
        }
    }

    public void ActivatePlacer(PlacementBoard placementBoard)
    {
        container.gameObject.SetActive(true);
        IsActive = true;
        if (arcLine != null) arcLine.enabled = true;
    }

    public void DeactivatePlacer()
    {
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
        foreach (var pickableObject in objectContainer.ItemsHeld)
        {
            if (pickableObject.ItemData == heldObject)
            {
                return pickableObject.gameObject;
            }
        }

        return null;
    }
}
