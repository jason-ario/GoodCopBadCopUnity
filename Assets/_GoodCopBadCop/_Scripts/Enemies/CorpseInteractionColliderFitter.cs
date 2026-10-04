using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Keeps a trigger <see cref="BoxCollider"/> wrapped around a corpse's current skeletal pose so the
/// interaction ray hits where the body actually lies.
///
/// A mutant's corpse interaction collider is authored for the living, standing body (an upright
/// capsule at the root). After a death animation or ragdoll lays the body down, that capsule is a
/// pillar of empty air over part of the body, and quadrupeds like the horse leave most of their
/// length outside it. This component replaces it with a box fitted to the skinned bones, padded
/// out to approximate the flesh around them, and refits periodically while the pose settles.
///
/// Lives on the same GameObject as the corpse's <see cref="Interactable"/> so
/// <c>PlayerInteractionController.ResolveInteractable</c> resolves the box directly.
/// </summary>
[DisallowMultipleComponent]
public class CorpseInteractionColliderFitter : MonoBehaviour
{
    private const float FastRefitInterval = 0.1f;
    private const float FastRefitDuration = 4f;
    private const float SlowRefitInterval = 0.5f;
    private const float MinLocalExtent = 0.05f;

    private BoxCollider _box;
    private Transform[] _bones;
    private float _padding;
    private Coroutine _refitRoutine;

    /// <summary>
    /// Fits a trigger box to the skinned bones under this GameObject and keeps it fitted. Disables
    /// <paramref name="source"/> (the authored interaction collider) when the box takes over.
    /// Returns the box, or null if there are no skinned bones to fit (the caller should keep using
    /// <paramref name="source"/>).
    /// </summary>
    public BoxCollider Begin(Collider source, float padding)
    {
        _padding = Mathf.Max(0f, padding);
        _bones = CollectSkinnedBones();
        if (_bones.Length == 0)
            return null;

        if (_box == null)
        {
            _box = gameObject.AddComponent<BoxCollider>();
            _box.isTrigger = true;
        }

        if (source != null && source != _box)
            source.enabled = false;

        _box.enabled = true;
        Refit();

        if (_refitRoutine != null)
            StopCoroutine(_refitRoutine);
        _refitRoutine = StartCoroutine(RefitLoop());

        return _box;
    }

    private void OnDisable()
    {
        _refitRoutine = null;
    }

    /// <summary>
    /// Refits quickly while the death animation or ragdoll is moving, then slowly for the rest of
    /// the corpse's life. The slow pass covers Animators that only advance once on screen, and
    /// bodies nudged later. It walks a few dozen transforms, so the cost is negligible.
    /// </summary>
    private IEnumerator RefitLoop()
    {
        float elapsed = 0f;
        while (elapsed < FastRefitDuration)
        {
            yield return new WaitForSeconds(FastRefitInterval);
            elapsed += FastRefitInterval;
            Refit();
        }

        var slowWait = new WaitForSeconds(SlowRefitInterval);
        while (true)
        {
            yield return slowWait;
            Refit();
        }
    }

    private Transform[] CollectSkinnedBones()
    {
        var bones = new HashSet<Transform>();
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(false))
        {
            if (!smr.enabled || smr.bones == null)
                continue;

            foreach (Transform bone in smr.bones)
            {
                if (bone != null)
                    bones.Add(bone);
            }
        }

        var result = new Transform[bones.Count];
        bones.CopyTo(result);
        return result;
    }

    private void Refit()
    {
        if (_box == null || _bones == null)
            return;

        Transform root = transform;
        Vector3 min = Vector3.positiveInfinity;
        Vector3 max = Vector3.negativeInfinity;
        bool any = false;

        foreach (Transform bone in _bones)
        {
            if (bone == null || !bone.gameObject.activeInHierarchy)
                continue;

            Vector3 p = root.InverseTransformPoint(bone.position);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
            any = true;
        }

        if (!any)
            return;

        // Bones sit at joint centres; pad by a world-space distance to reach the body surface.
        Vector3 scale = root.lossyScale;
        Vector3 pad = new Vector3(
            _padding / Mathf.Max(Mathf.Abs(scale.x), 0.0001f),
            _padding / Mathf.Max(Mathf.Abs(scale.y), 0.0001f),
            _padding / Mathf.Max(Mathf.Abs(scale.z), 0.0001f));

        min -= pad;
        max += pad;

        _box.center = (min + max) * 0.5f;
        _box.size = Vector3.Max(max - min, Vector3.one * MinLocalExtent);
    }
}
