using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable "limb reactivity" system for any animated enemy.
///
/// 1. HITBOXES — turns the rig's limb colliders (by default the Ragdoll Builder's
///    <see cref="RagdollBone"/> colliders) into enabled trigger hitboxes while the character is alive,
///    tagged with <see cref="LimbHitbox"/>. Their rigidbodies stay kinematic, so the hitboxes simply
///    follow the animated bones. Weapon hit-scans (Pistol, Shotgun, MeleeWeaponHitbox) query triggers,
///    so every limb becomes hittable, and <see cref="CombatHitUtility"/> prefers these precise limb hits
///    over a broad root capsule.
///
/// 2. REACTION — <see cref="ReactToHit"/> finds the limb nearest the impact and kicks it with a
///    damped angular spring, rotating it around the torque axis (lever × hit direction). A fraction
///    of the kick ripples up to parent limbs. The offset is layered on top of the final pose in
///    LateUpdate (after Animator, Animation Rigging and FIMSpace animators) and undone again in the
///    next Update, so it never accumulates on bones that aren't keyed by the Animator.
///
/// This component is purely local/cosmetic. Networked callers (e.g. <see cref="MutantEnemy"/>) relay
/// the hit point/direction to every client and call <see cref="ReactToHit"/> there.
///
/// SETUP: add to the enemy root (or the rig root). If the rig has RagdollBone colliders, that's all
/// you need. Otherwise set Limb Source to Explicit List and assign the limb colliders.
/// </summary>
[DefaultExecutionOrder(32000)]
[DisallowMultipleComponent]
public class LimbHitReactor : MonoBehaviour
{
    public enum LimbSource
    {
        /// <summary>Every collider sitting on a <see cref="RagdollBone"/> under <see cref="_rigRoot"/>.</summary>
        RagdollBones = 0,
        /// <summary>Exactly the colliders listed in <see cref="_explicitLimbColliders"/>.</summary>
        ExplicitList = 1,
    }

    [Header("Limb Hitboxes")]
    [Tooltip("Where limb colliders come from. RagdollBones picks up every Ragdoll Builder collider automatically.")]
    [SerializeField] private LimbSource _limbSource = LimbSource.RagdollBones;

    [Tooltip("Root searched for limbs (RagdollBones mode). Defaults to this transform.")]
    [SerializeField] private Transform _rigRoot;

    [Tooltip("Limb colliders used in Explicit List mode. Each collider's transform is the bone that reacts.")]
    [SerializeField] private Collider[] _explicitLimbColliders;

    [Tooltip("Enable the limb colliders as trigger hitboxes on Start (after RagdollController has disabled them in Awake).")]
    [SerializeField] private bool _activateHitboxesOnStart = true;

    [Tooltip("Scales limb collider thickness while acting as hitboxes, for more forgiving hits on thin limbs. " +
             "The original size is restored on Shutdown, before the ragdoll needs it.")]
    [SerializeField, Min(1f)] private float _hitboxInflation = 1f;

    [Header("Reaction")]
    [Tooltip("Rough peak swing (degrees) for a strength-1 hit on the struck limb.")]
    [SerializeField] private float _kickDegrees = 20f;

    [Tooltip("Hard cap on any limb's reaction angle (degrees).")]
    [SerializeField] private float _maxAngle = 40f;

    [Tooltip("Spring stiffness pulling the limb back to its animated pose. Higher = snappier.")]
    [SerializeField] private float _stiffness = 160f;

    [Tooltip("Spring damping. ~2*sqrt(stiffness) is critically damped; lower = more wobble.")]
    [SerializeField] private float _damping = 14f;

    [Tooltip("Fraction of the kick passed to each parent limb up the chain.")]
    [SerializeField, Range(0f, 1f)] private float _parentFalloff = 0.45f;

    [Tooltip("How many parent limbs the kick ripples up through.")]
    [SerializeField, Range(0, 6)] private int _parentPropagationDepth = 2;

    [Tooltip("Damage that maps to strength 1 in StrengthFromDamage.")]
    [SerializeField, Min(0.01f)] private float _referenceDamage = 25f;

    [Tooltip("Min/max strength StrengthFromDamage can return.")]
    [SerializeField] private Vector2 _strengthRange = new Vector2(0.6f, 1.75f);

    [Tooltip("Hits farther than this (metres) from every limb are ignored.")]
    [SerializeField] private float _maxLimbDistance = 3f;

    // ── Runtime state ──────────────────────────────────────────────────────────

    private Collider[]   _colliders = new Collider[0];
    private Transform[]  _bones     = new Transform[0];
    private Rigidbody[]  _bodies    = new Rigidbody[0];
    private int[]        _parent    = new int[0];
    private Vector3[]    _rot       = new Vector3[0]; // world-space rotation vector (axis * degrees)
    private Vector3[]    _vel       = new Vector3[0]; // degrees / second
    private Quaternion[] _restoreLocal = new Quaternion[0];
    private bool[]       _applied   = new bool[0];

    private readonly List<ColliderSize> _originalSizes = new List<ColliderSize>();

    private bool _initialised;
    private bool _hitboxesActive;
    private bool _reactionsEnabled = true;
    private bool _anyMotion;
    private bool _anyApplied;

    private const float RestEpsilon = 0.05f;
    private const float MaxStep = 1f / 60f;

    public bool HitboxesActive => _hitboxesActive;
    public int LimbCount => _bones.Length;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    private void Awake() => Initialise();

    private void Start()
    {
        if (_activateHitboxesOnStart)
            SetHitboxesActive(true);
    }

    private void OnDisable()
    {
        RestorePose();
        ClearMotion();
    }

    private void Initialise()
    {
        if (_initialised) return;
        _initialised = true;

        Transform root = _rigRoot != null ? _rigRoot : transform;
        List<Collider> found = new List<Collider>();

        if (_limbSource == LimbSource.ExplicitList)
        {
            if (_explicitLimbColliders != null)
                foreach (Collider c in _explicitLimbColliders)
                    if (c != null && !found.Contains(c)) found.Add(c);
        }
        else
        {
            foreach (RagdollBone bone in root.GetComponentsInChildren<RagdollBone>(true))
            {
                Collider c = bone.GetComponent<Collider>();
                if (c != null && c.gameObject != gameObject && !found.Contains(c)) found.Add(c);
            }
        }

        // Parents before children, so parent offsets are applied first each frame.
        found.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));

        int n = found.Count;
        _colliders    = found.ToArray();
        _bones        = new Transform[n];
        _bodies       = new Rigidbody[n];
        _parent       = new int[n];
        _rot          = new Vector3[n];
        _vel          = new Vector3[n];
        _restoreLocal = new Quaternion[n];
        _applied      = new bool[n];

        Dictionary<Transform, int> indexOf = new Dictionary<Transform, int>(n);
        for (int i = 0; i < n; i++)
        {
            _bones[i]  = _colliders[i].transform;
            _bodies[i] = _colliders[i].attachedRigidbody;
            if (!indexOf.ContainsKey(_bones[i])) indexOf[_bones[i]] = i;

            LimbHitbox marker = _colliders[i].GetComponent<LimbHitbox>();
            if (marker == null) marker = _colliders[i].gameObject.AddComponent<LimbHitbox>();
            marker.Bind(this, i, _colliders[i]);

            _originalSizes.Add(ColliderSize.Capture(_colliders[i]));
        }

        for (int i = 0; i < n; i++)
        {
            _parent[i] = -1;
            for (Transform t = _bones[i].parent; t != null && t != root.parent; t = t.parent)
            {
                if (indexOf.TryGetValue(t, out int p)) { _parent[i] = p; break; }
            }
        }

        if (n == 0)
            Debug.LogWarning($"[LimbHitReactor] '{name}' found no limb colliders ({_limbSource}).", this);
    }

    private static int Depth(Transform t)
    {
        int d = 0;
        for (; t != null; t = t.parent) d++;
        return d;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    /// <summary>
    /// Enables the limb colliders as kinematic trigger hitboxes (inflated by Hitbox Inflation), or
    /// disables them and restores their original size. Local only — call on every peer.
    /// </summary>
    public void SetHitboxesActive(bool active)
    {
        Initialise();
        _hitboxesActive = active;

        for (int i = 0; i < _colliders.Length; i++)
        {
            Collider c = _colliders[i];
            if (c == null) continue;

            _originalSizes[i].Apply(c, active ? _hitboxInflation : 1f);
            c.isTrigger = active;
            c.enabled   = active;
        }
    }

    /// <summary>
    /// Stops all reactions, snaps limbs back to their animated pose, and turns the hitboxes off
    /// (restoring original collider sizes). Call on death before a ragdoll takes over.
    /// </summary>
    public void Shutdown()
    {
        _reactionsEnabled = false;
        RestorePose();
        ClearMotion();
        if (_hitboxesActive) SetHitboxesActive(false);
    }

    /// <summary>Re-arms reactions and hitboxes after <see cref="Shutdown"/> (e.g. a revived enemy).</summary>
    public void Rearm()
    {
        _reactionsEnabled = true;
        SetHitboxesActive(true);
    }

    /// <summary>Maps raw damage to a reaction strength using Reference Damage and Strength Range.</summary>
    public float StrengthFromDamage(float damage) =>
        Mathf.Clamp(damage / _referenceDamage, _strengthRange.x, _strengthRange.y);

    /// <summary>
    /// Kicks the limb nearest <paramref name="hitPoint"/> away along <paramref name="direction"/>.
    /// Pass a zero direction to push straight into the limb from the impact point.
    /// </summary>
    public void ReactToHit(Vector3 hitPoint, Vector3 direction, float strength = 1f)
    {
        Initialise();
        if (!_reactionsEnabled || !isActiveAndEnabled || _bones.Length == 0) return;

        int limb = FindClosestLimb(hitPoint, out float distance);
        if (limb < 0 || distance > _maxLimbDistance) return;

        ReactOnLimb(limb, hitPoint, direction, strength);
    }

    /// <summary>Kicks a specific limb (e.g. from a <see cref="LimbHitbox"/>).</summary>
    public void ReactOnLimb(int limb, Vector3 hitPoint, Vector3 direction, float strength = 1f)
    {
        if (!_reactionsEnabled || limb < 0 || limb >= _bones.Length || _bones[limb] == null) return;

        Transform bone = _bones[limb];
        Vector3 dir = direction.sqrMagnitude > 1e-6f ? direction.normalized : (bone.position - hitPoint).normalized;
        if (dir.sqrMagnitude < 1e-6f) return;

        Vector3 lever = hitPoint - bone.position;
        if (lever.sqrMagnitude < 1e-6f) lever = bone.up;

        Vector3 axis = Vector3.Cross(lever, dir);
        if (axis.sqrMagnitude < 1e-8f) axis = Vector3.Cross(bone.up, dir);
        if (axis.sqrMagnitude < 1e-8f) return;
        axis.Normalize();

        // Initial angular speed that peaks near _kickDegrees for a ~critically damped spring.
        float speed = _kickDegrees * Mathf.Max(0f, strength) * Mathf.Sqrt(_stiffness) * 2.2f;

        int current = limb;
        float weight = 1f;
        for (int depth = 0; current >= 0 && depth <= _parentPropagationDepth; depth++)
        {
            _vel[current] += axis * (speed * weight);
            weight *= _parentFalloff;
            current = _parent[current];
        }

        _anyMotion = true;
    }

    // ── Simulation ────────────────────────────────────────────────────────────

    private void Update()
    {
        // Undo last frame's offset before anything animates this frame, so bones not keyed by the
        // Animator (or driven by tail/IK solvers that read the current pose) never accumulate it.
        RestorePose();
    }

    private void LateUpdate()
    {
        if (!_anyMotion || !_reactionsEnabled) return;

        float dt = Time.deltaTime;
        if (dt <= 0f) return;

        int steps = Mathf.Max(1, Mathf.CeilToInt(dt / MaxStep));
        float h = dt / steps;

        bool stillMoving = false;
        for (int i = 0; i < _bones.Length; i++)
        {
            if (_rot[i] == Vector3.zero && _vel[i] == Vector3.zero) continue;

            for (int s = 0; s < steps; s++)
            {
                Vector3 acc = -_stiffness * _rot[i] - _damping * _vel[i];
                _vel[i] += acc * h;
                _rot[i] += _vel[i] * h;
            }

            if (_rot[i].sqrMagnitude > _maxAngle * _maxAngle)
                _rot[i] = _rot[i].normalized * _maxAngle;

            if (_rot[i].sqrMagnitude < RestEpsilon * RestEpsilon && _vel[i].sqrMagnitude < RestEpsilon * RestEpsilon)
            {
                _rot[i] = Vector3.zero;
                _vel[i] = Vector3.zero;
                continue;
            }

            stillMoving = true;
        }

        _anyMotion = stillMoving;
        if (!stillMoving) return;

        // Parents first (array is depth-sorted). Changing a parent's world rotation never changes a
        // child's localRotation, so recording-then-applying per limb restores cleanly.
        for (int i = 0; i < _bones.Length; i++)
        {
            Vector3 r = _rot[i];
            if (r == Vector3.zero) continue;

            Transform bone = _bones[i];
            if (bone == null) continue;

            // A dynamic rigidbody means the ragdoll owns this bone now.
            Rigidbody rb = _bodies[i];
            if (rb != null && !rb.isKinematic) continue;

            float angle = r.magnitude;
            _restoreLocal[i] = bone.localRotation;
            _applied[i] = true;
            _anyApplied = true;
            bone.rotation = Quaternion.AngleAxis(angle, r / angle) * bone.rotation;
        }
    }

    private void RestorePose()
    {
        if (!_anyApplied) return;
        for (int i = 0; i < _bones.Length; i++)
        {
            if (!_applied[i]) continue;
            _applied[i] = false;
            if (_bones[i] != null) _bones[i].localRotation = _restoreLocal[i];
        }
        _anyApplied = false;
    }

    private void ClearMotion()
    {
        for (int i = 0; i < _rot.Length; i++)
        {
            _rot[i] = Vector3.zero;
            _vel[i] = Vector3.zero;
        }
        _anyMotion = false;
    }

    private int FindClosestLimb(Vector3 point, out float distance)
    {
        int best = -1;
        float bestSqr = float.MaxValue;

        for (int i = 0; i < _colliders.Length; i++)
        {
            Collider c = _colliders[i];
            if (c == null) continue;

            Vector3 closest = c.enabled ? c.ClosestPoint(point) : c.transform.position;
            float sqr = (closest - point).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = i;
            }
        }

        distance = best >= 0 ? Mathf.Sqrt(bestSqr) : float.MaxValue;
        return best;
    }

    // ── Collider size cache (for hitbox inflation) ─────────────────────────────

    private readonly struct ColliderSize
    {
        private readonly float _radius;
        private readonly float _height;
        private readonly Vector3 _size;

        private ColliderSize(float radius, float height, Vector3 size)
        {
            _radius = radius;
            _height = height;
            _size   = size;
        }

        public static ColliderSize Capture(Collider c)
        {
            switch (c)
            {
                case CapsuleCollider cap: return new ColliderSize(cap.radius, cap.height, Vector3.zero);
                case SphereCollider sph:  return new ColliderSize(sph.radius, 0f, Vector3.zero);
                case BoxCollider box:     return new ColliderSize(0f, 0f, box.size);
                default:                  return default;
            }
        }

        /// <summary>Thickens the collider by <paramref name="scale"/> (capsule length grows only by the added radius).</summary>
        public void Apply(Collider c, float scale)
        {
            switch (c)
            {
                case CapsuleCollider cap:
                    cap.radius = _radius * scale;
                    cap.height = _height + 2f * (_radius * scale - _radius);
                    break;
                case SphereCollider sph:
                    sph.radius = _radius * scale;
                    break;
                case BoxCollider box:
                    box.size = _size * scale;
                    break;
            }
        }
    }
}
