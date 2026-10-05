using UnityEngine;

/// <summary>
/// Cosmetic reaction for static props (signs, poles, etc.) that should respond when struck by a
/// bullet or melee swing: plays a spatial hit sound and applies a damped spring wobble/spin.
///
/// Purely visual — no NetworkObject required. The attacking client calls <see cref="TryHitAt"/>
/// locally for instant feedback, and the weapon relays the hit point through its own RPCs so every
/// other client resolves and plays the same reaction (see <c>Pistol</c> / <c>MeleeWeaponHitbox</c>).
/// Requires a (non-trigger) collider on this object or a child so weapons can detect it.
/// </summary>
public class HittableProp : MonoBehaviour
{
    // Configuration

    [Tooltip("Transform that wobbles. Defaults to this transform. Rotates around its own pivot.")]
    [SerializeField] private Transform _visualRoot;

    [Header("Audio")]
    [SerializeField] private AudioClip _hitSound;
    [SerializeField, Range(0f, 1f)] private float _volume = 0.8f;
    [SerializeField] private Vector2 _pitchRange = new Vector2(0.9f, 1.1f);
    [SerializeField] private float _soundMaxDistance = 20f;

    [Header("Wobble")]
    [Tooltip("Angular velocity (deg/s) added around the world up axis per hit.")]
    [SerializeField] private float _spinImpulse = 220f;

    [Tooltip("Angular velocity (deg/s) added to tip the prop away from the hit per hit.")]
    [SerializeField] private float _tiltImpulse = 110f;

    [Tooltip("Spring stiffness. Higher = faster, snappier oscillation.")]
    [SerializeField] private float _stiffness = 70f;

    [Tooltip("Spring damping. Higher = settles sooner.")]
    [SerializeField] private float _damping = 5f;

    [Tooltip("Hard clamp on any wobble angle, in degrees.")]
    [SerializeField] private float _maxAngle = 35f;

    [Tooltip("Minimum seconds between reactions, to ignore duplicate reports of the same hit.")]
    [SerializeField] private float _minHitInterval = 0.05f;


    // Internal

    private const float SleepThreshold = 0.01f;
    private static readonly Collider[] OverlapBuffer = new Collider[8];

    private Quaternion _restLocalRotation;
    private Vector3 _angle;     // x = pitch (world X), y = yaw (world Y), z = roll (world Z), degrees
    private Vector3 _velocity;  // deg/s
    private float _lastHitTime = -1f;


    // Lifecycle

    private void Awake()
    {
        if (_visualRoot == null) _visualRoot = transform;
        _restLocalRotation = _visualRoot.localRotation;
        enabled = false;
    }

    private void OnDisable()
    {
        _angle = _velocity = Vector3.zero;
        if (_visualRoot != null) _visualRoot.localRotation = _restLocalRotation;
    }

    private void Update()
    {
        float dt = Mathf.Min(Time.deltaTime, 1f / 30f);

        Vector3 accel = -_stiffness * _angle - _damping * _velocity;
        _velocity += accel * dt;
        _angle += _velocity * dt;
        _angle = Vector3.Max(Vector3.one * -_maxAngle, Vector3.Min(Vector3.one * _maxAngle, _angle));

        if (_angle.sqrMagnitude < SleepThreshold && _velocity.sqrMagnitude < SleepThreshold)
        {
            enabled = false; // OnDisable restores the rest pose.
            return;
        }

        Transform parent = _visualRoot.parent;
        Quaternion restWorld = parent != null ? parent.rotation * _restLocalRotation : _restLocalRotation;
        Quaternion offset = Quaternion.AngleAxis(_angle.y, Vector3.up)
                          * Quaternion.AngleAxis(_angle.x, Vector3.right)
                          * Quaternion.AngleAxis(_angle.z, Vector3.forward);
        _visualRoot.rotation = offset * restWorld;
    }


    // Public API

    /// <summary>
    /// Plays the hit sound and kicks the wobble spring. Subclasses (e.g. <see cref="ShatterableProp"/>)
    /// override this to swap the wobble for a different reaction while reusing the weapon hit pipeline.
    /// </summary>
    public virtual void Hit(Vector3 hitPoint, Vector3 direction)
    {
        if (Time.time - _lastHitTime < _minHitInterval) return;
        _lastHitTime = Time.time;

        PlayHitSound(hitPoint);

        Vector3 dir = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (dir.sqrMagnitude < 0.0001f) dir = Random.insideUnitCircle.normalized;
        dir.Normalize();

        // Spin direction from which side of the pivot was struck (torque about world up).
        Vector3 lever = hitPoint - _visualRoot.position;
        float torque = Vector3.Cross(lever, dir).y;
        float spinSign = Mathf.Abs(torque) > 0.001f ? Mathf.Sign(torque) : (Random.value < 0.5f ? -1f : 1f);

        // Tip away from the hit: rotation axis perpendicular to up and the hit direction.
        Vector3 tiltAxis = Vector3.Cross(Vector3.up, dir);

        _velocity.y += spinSign * _spinImpulse;
        _velocity.x += tiltAxis.x * _tiltImpulse;
        _velocity.z += tiltAxis.z * _tiltImpulse;

        enabled = true;
    }

    /// <summary>Plays the configured hit sound at <paramref name="point"/> with a randomised pitch.</summary>
    protected void PlayHitSound(Vector3 point)
    {
        if (_hitSound == null || SFXController.Instance == null) return;

        float pitch = Random.Range(_pitchRange.x, _pitchRange.y);
        SFXController.Instance.PlayAtPosition(_hitSound, point, _volume, pitch, _soundMaxDistance);
    }

    /// <summary>
    /// Finds a <see cref="HittableProp"/> whose collider is at/near <paramref name="point"/> and
    /// triggers it. Used both by the attacker locally and by remote clients replaying a relayed hit.
    /// </summary>
    public static bool TryHitAt(Vector3 point, Vector3 direction, float radius = 0.3f)
    {
        int count = Physics.OverlapSphereNonAlloc(point, radius, OverlapBuffer, Physics.AllLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            HittableProp prop = OverlapBuffer[i] != null ? OverlapBuffer[i].GetComponentInParent<HittableProp>() : null;
            if (prop != null)
            {
                prop.Hit(point, direction);
                return true;
            }
        }
        return false;
    }
}
