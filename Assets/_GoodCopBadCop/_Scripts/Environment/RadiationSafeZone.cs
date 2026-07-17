using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Marks a trigger volume as a radiation safe zone — players inside are exempt
/// from the off-trail radiation penalty applied by <see cref="OffTrailRadiation"/>.
///
/// Does NOT reduce or remove radiation on its own; pair with a <see cref="RadiationHotspot"/>
/// set to a negative rate, or with pill/treatment logic, if active cleansing is needed.
///
/// Register this component in the <see cref="OffTrailRadiation.SafeZones"/> list on the
/// OffTrailRadiation GameObject to connect the two systems.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RadiationSafeZone : MonoBehaviour
{
    [Header("Events")]
    public UnityEvent OnPlayerEnter;
    public UnityEvent OnPlayerExit;

    // Players currently inside this volume.
    private readonly HashSet<PlayerRadiation> _playersInside = new();

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
    }

    /// <summary>Returns true if <paramref name="player"/> is currently inside this zone.</summary>
    public bool Contains(PlayerRadiation player) => _playersInside.Contains(player);

    private void OnTriggerEnter(Collider other)
    {
        PlayerRadiation radiation = other.GetComponentInParent<PlayerRadiation>();
        if (radiation == null) return;

        _playersInside.Add(radiation);
        OnPlayerEnter?.Invoke();

        Debug.Log($"[RadiationSafeZone] Player entered safe zone '{gameObject.name}'.");
    }

    private void OnTriggerExit(Collider other)
    {
        PlayerRadiation radiation = other.GetComponentInParent<PlayerRadiation>();
        if (radiation == null) return;

        _playersInside.Remove(radiation);
        OnPlayerExit?.Invoke();

        Debug.Log($"[RadiationSafeZone] Player exited safe zone '{gameObject.name}'.");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.12f);
        Collider col = GetComponent<Collider>();
        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.7f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = new Color(0f, 0.6f, 1f, 0.7f);
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
