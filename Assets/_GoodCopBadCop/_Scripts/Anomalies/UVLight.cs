using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Marks a GameObject as an active UV light source for the blue veins anomaly system.
/// Registers itself into a shared static list on OnEnable and removes itself on OnDisable,
/// so the flashlight script only needs to enable/disable this component to participate
/// in the reveal effect — no direct references to BlueVeinsAnomaly are required.
///
/// The reveal radius is read from a SphereCollider on the same GameObject if present,
/// otherwise it uses the serialized fallbackRadius field.
/// </summary>
[ExecuteAlways]
public class UVLight : MonoBehaviour
{
    /// <summary>All currently active UV light sources across the scene.</summary>
    public static readonly List<UVLight> ActiveLights = new();

    [Tooltip("Radius of the reveal sphere. Auto-read from a SphereCollider on this GameObject if one is present.")]
    [SerializeField] private float fallbackRadius = 1f;

    /// <summary>World-space position of this UV light this frame.</summary>
    public Vector3 Position => transform.position;

    /// <summary>World-space reveal radius, accounting for lossy scale.</summary>
    public float Radius => GetRadius();

    private void OnEnable()
    {
        if (!ActiveLights.Contains(this))
            ActiveLights.Add(this);
    }

    private void OnDisable()
    {
        ActiveLights.Remove(this);
    }

    private float GetRadius()
    {
        if (TryGetComponent(out SphereCollider sphere))
            return sphere.radius * Mathf.Max(transform.lossyScale.x, transform.lossyScale.y, transform.lossyScale.z);

        return fallbackRadius;
    }

#if UNITY_EDITOR
    // Draw a wire sphere in the Scene view so the radius is easy to tune.
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.4f, 0.4f, 1f, 0.4f);
        Gizmos.DrawWireSphere(transform.position, GetRadius());
    }
#endif
}
