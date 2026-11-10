using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Solid physical wall (blocks players and thrown/dropped rigidbodies) that raycasts treat as
/// if it weren't there. Example: the booth window, so players can't climb out and items can't
/// be thrown out, while interaction, tools and weapons still work through it.
///
/// How each query family ignores it:
/// - Default-mask queries (thermometer, heart-rate / radiation scanners, mutant line of sight) and
///   the Player's <c>interactLayer</c> skip it because Awake moves it to the "Ignore Raycast" layer.
/// - Weapon hitscan / tracers / melee line of sight skip it via the required
///   <see cref="BulletPassthrough"/> marker (<see cref="CombatHitUtility.IsBulletPassthrough"/>).
/// - Other all-layer single raycasts (placement, melee enemy reticle, dialogue camera dolly)
///   use <see cref="Raycast"/>, which returns the nearest hit that isn't a blocker.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(BulletPassthrough))]
public class RaycastTransparentBlocker : MonoBehaviour
{
    private static readonly HashSet<Collider> s_colliders = new HashSet<Collider>();
    private static readonly RaycastHit[] s_hitBuffer = new RaycastHit[32];

    private Collider[] _colliders;

    private void Awake()
    {
        int ignoreRaycastLayer = LayerMask.NameToLayer("Ignore Raycast");
        _colliders = GetComponentsInChildren<Collider>(true);
        foreach (Collider col in _colliders)
        {
            if (col.isTrigger)
                Debug.LogWarning($"[RaycastTransparentBlocker] '{col.name}' is a trigger and will not block anything.", col);
            if (ignoreRaycastLayer >= 0)
                col.gameObject.layer = ignoreRaycastLayer;
        }
    }

    private void OnEnable()
    {
        foreach (Collider col in _colliders)
            if (col != null) s_colliders.Add(col);
    }

    private void OnDisable()
    {
        foreach (Collider col in _colliders)
            if (col != null) s_colliders.Remove(col);
    }

    /// <summary>True if <paramref name="col"/> belongs to an active blocker.</summary>
    public static bool IsBlocker(Collider col) => col != null && s_colliders.Contains(col);

    /// <summary>
    /// Physics.Raycast that ignores blockers. Same cost as a plain raycast unless the first hit
    /// is a blocker, in which case it re-queries and returns the nearest non-blocker hit.
    /// </summary>
    public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance,
        int layerMask, QueryTriggerInteraction triggerInteraction)
    {
        if (!Physics.Raycast(origin, direction, out hit, maxDistance, layerMask, triggerInteraction))
            return false;
        if (!s_colliders.Contains(hit.collider))
            return true;

        int count = Physics.RaycastNonAlloc(origin, direction, s_hitBuffer, maxDistance, layerMask, triggerInteraction);
        bool found = false;
        float best = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            RaycastHit h = s_hitBuffer[i];
            if (s_colliders.Contains(h.collider) || h.distance >= best) continue;
            best = h.distance;
            hit = h;
            found = true;
        }
        if (!found) hit = default;
        return found;
    }

    public static bool Raycast(Ray ray, out RaycastHit hit, float maxDistance, int layerMask,
        QueryTriggerInteraction triggerInteraction)
        => Raycast(ray.origin, ray.direction, out hit, maxDistance, layerMask, triggerInteraction);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => s_colliders.Clear();

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.4f, 0f, 0.35f);
        foreach (BoxCollider box in GetComponentsInChildren<BoxCollider>(true))
        {
            Gizmos.matrix = box.transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.DrawWireCube(box.center, box.size);
        }
    }
#endif
}
