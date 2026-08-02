using System;
using System.Collections;
using System.Collections.Generic;
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

    [SerializeField] private Color inRangeColor = Color.white;
    [SerializeField] private Color outOfRangeColor = Color.red;

    [Header("Ghost Tint")]
    [SerializeField] private Color ghostInRangeColor = new Color(0f, 1f, 0f, 0.5f);
    [SerializeField] private Color ghostOutOfRangeColor = new Color(1f, 0f, 0f, 0.5f);

    [Header("Placement Feedback")]
    [SerializeField] private PlacementFeedback _placementFeedback;
    public PlacementFeedback PlacementFeedback => _placementFeedback;

    private PickableItemData _pickableItemData;
    private PickableObject _clonedItem;
    private PlacementBoard _currentPlacementBoard;

    /// <summary>
    /// True while the current placement board is a <see cref="PlacementSlot"/> — an exact,
    /// unambiguous fixed pose (e.g. a mail cubby's snap point) makes the trajectory arc redundant
    /// noise, unlike free-surface placement where it helps show where the item will land.
    /// </summary>
    private bool _suppressArc;
    private readonly List<Material> _ghostMaterials = new List<Material>();
    public bool IsActive;
    public bool IsInRange { get; private set; } = true;

    /// <summary>
    /// True for one frame after DeactivatePlacer is called, AND the placer was in range when it was deactivated.
    /// Used by PlayerPickupController to confirm a valid drop on right-click release.
    /// </summary>
    public bool deactivatedThisFrame = false;
    private bool _wasInRangeWhenDeactivated = false;
    public bool WasInRangeWhenDeactivated => _wasInRangeWhenDeactivated;

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

        if (IsActive && !_suppressArc)
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
        if (slotTransform == null && sourceItem.GetComponentInChildren<PlacementAnchor>(true) == null) return;

        // Spawn clone parented to the container. An optional anchor authored in the
        // pickup prefab overrides the hand slot pose for surface placement.
        _clonedItem = Instantiate(sourceItem, container);
        if (TryGetPlacementPose(sourceItem, out Vector3 placementPosition, out Quaternion placementRotation))
        {
            _clonedItem.transform.SetPositionAndRotation(placementPosition, placementRotation);
        }
        else
        {
            _clonedItem.transform.localPosition = slotTransform.localPosition;
            _clonedItem.transform.localRotation = slotTransform.localRotation;
        }
        _clonedItem.GetComponent<PickableObject>().SetPlacementClone();
        _clonedItem.GetComponent<PickableObject>().OnSpawnedAsPlacementClone();

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

        DisableGhostRopes(_clonedItem.gameObject);
        SetupGhostMaterials(_clonedItem.gameObject);
    }

    /// <summary>
    /// Previously disabled all colliders on the ghost clone. Collider state is now
    /// managed by <see cref="PickableObject.SetPlacementClone"/>: physics colliders
    /// are set as triggers, InteractableCollider children are disabled.
    /// Method retained to avoid breaking any subclass or external references.
    /// </summary>
    private void DisableAllColliders(GameObject root)
    {
        foreach (Collider col in root.GetComponentsInChildren<Collider>(true))
            col.enabled = false;
    }

    private void DisableGhostRopes(GameObject root)
    {
        foreach (GogoGaga.OptimizedRopesAndCables.Rope rope in root.GetComponentsInChildren<GogoGaga.OptimizedRopesAndCables.Rope>(true))
        {
            rope.enabled = false;

            LineRenderer ropeRenderer = rope.GetComponent<LineRenderer>();
            if (ropeRenderer != null) ropeRenderer.enabled = false;
        }
    }

    private static readonly int SurfaceProperty = Shader.PropertyToID("_Surface");
    private static readonly int BlendProperty = Shader.PropertyToID("_Blend");
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int SrcBlendProperty = Shader.PropertyToID("_SrcBlend");
    private static readonly int DstBlendProperty = Shader.PropertyToID("_DstBlend");
    private static readonly int ZWriteProperty = Shader.PropertyToID("_ZWrite");

    /// <summary>
    /// Replaces every renderer's materials on the ghost clone with transparent
    /// material instances tinted by <see cref="ghostInRangeColor"/>, and caches
    /// them so <see cref="SetInRange"/> can update their tint each frame.
    /// </summary>
    private void SetupGhostMaterials(GameObject root)
    {
        _ghostMaterials.Clear();

        foreach (Renderer rend in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] instanceMats = rend.materials;
            for (int i = 0; i < instanceMats.Length; i++)
            {
                Material mat = new Material(instanceMats[i]);

                // Switch URP surface type to Transparent
                mat.SetFloat(SurfaceProperty, 1f);   // 1 = Transparent
                mat.SetFloat(BlendProperty, 0f);     // 0 = Alpha blend
                mat.SetFloat(ZWriteProperty, 0f);
                mat.SetInt(SrcBlendProperty, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                mat.SetInt(DstBlendProperty, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

                mat.SetColor(BaseColorProperty, ghostInRangeColor);

                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;

                instanceMats[i] = mat;
                _ghostMaterials.Add(mat);
            }
            rend.materials = instanceMats;
        }
    }

    /// <summary>
    /// Activates the placer ghost at the current transform position.
    /// Pass null for placementBoard when placing on an arbitrary surface.
    /// </summary>
    public void ActivatePlacer(PlacementBoard placementBoard = null)
    {
        _currentPlacementBoard = placementBoard;
        _suppressArc = placementBoard is PlacementSlot;
        container.gameObject.SetActive(true);
        IsActive = true;

        SpawnClone();

        if (_clonedItem != null)
            _clonedItem.gameObject.SetActive(true);

        if (arcLine != null)
        {
            if (_suppressArc)
            {
                arcLine.positionCount = 0;
                arcLine.enabled = false;
            }
            else
            {
                arcLine.enabled = true;
            }
        }
    }

    public void DeactivatePlacer()
    {
        if (_clonedItem != null)
        {
            Destroy(_clonedItem.gameObject);
            _clonedItem = null;
        }

        _ghostMaterials.Clear();

        _wasInRangeWhenDeactivated = IsInRange;
        IsInRange = true;
        container.gameObject.SetActive(false);
        IsActive = false;
        _suppressArc = false;

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

    public bool TryGetPlacementPose(PickableObject sourceItem, out Vector3 position, out Quaternion rotation)
    {
        if (sourceItem == null)
        {
            position = default;
            rotation = default;
            return false;
        }

        // An active PlacementSlot (e.g. a mail cubby's exact snap point) is authoritative over
        // everything below — it is an explicit, unambiguous fixed pose. Without this check, an
        // item with no PlacementAnchor that happens to match an entry in the hand's ItemsHeld
        // array (e.g. a mail package sharing a generic hand-carry pose) would fall through to
        // that unrelated pose instead, so the ghost (SpawnClone) and the real drop position
        // (PlayerPickupController.DropObject) would both land away from the slot — only a
        // correctly-delivered package gets silently re-snapped afterward by MailCubbySlot, which
        // is why the mismatch only showed up for incorrect deliveries.
        if (_currentPlacementBoard is PlacementSlot activeSlot)
        {
            Transform snap = activeSlot.SnapPoint;
            position = snap.position;
            rotation = snap.rotation;
            return true;
        }

        PlacementAnchor anchor = sourceItem.GetComponentInChildren<PlacementAnchor>(true);
        if (anchor != null)
        {
            Transform sourceTransform = sourceItem.transform;
            Vector3 localAnchorPosition = sourceTransform.InverseTransformPoint(anchor.transform.position);
            Quaternion localAnchorRotation = Quaternion.Inverse(sourceTransform.rotation) * anchor.transform.rotation;

            rotation = transform.rotation * Quaternion.Inverse(localAnchorRotation);
            position = transform.position - rotation * Vector3.Scale(localAnchorPosition, sourceTransform.lossyScale);
            return true;
        }

        GameObject placementItem = GetPickableObject(sourceItem.ItemData);
        if (placementItem != null)
        {
            position = placementItem.transform.position;
            rotation = placementItem.transform.rotation;
            return true;
        }

        position = default;
        rotation = default;
        return false;
    }

    /// <summary>
    /// Tints the arc line and ghost clone green (in range, can place) or red (out of range, cannot place).
    /// Safe to call every frame while the placer is active.
    /// </summary>
    public void SetInRange(bool inRange)
    {
        IsInRange = inRange;

        if (arcLine != null)
        {
            Color lineColor = inRange ? inRangeColor : outOfRangeColor;
            arcLine.startColor = lineColor;
            arcLine.endColor = lineColor;
        }

        Color ghostColor = inRange ? ghostInRangeColor : ghostOutOfRangeColor;
        foreach (Material mat in _ghostMaterials)
        {
            if (mat != null)
                mat.SetColor(BaseColorProperty, ghostColor);
        }
    }
}
