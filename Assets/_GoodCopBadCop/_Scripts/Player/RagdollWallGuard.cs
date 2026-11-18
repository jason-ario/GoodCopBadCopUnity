using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Keeps a ragdoll on the correct side of thin world geometry (e.g. the ~0.3 m booth walls).
/// Used by <see cref="RagdollController"/> on the peer that simulates the ragdoll (the server).
///
/// Capped depenetration and continuous collision stop limbs from being launched through walls,
/// but not a body that is already partly inside or through a wall when it dies (a mutant
/// pressed against the booth wall, an arm reaching past it). PhysX then pushes it out the far
/// side and the joints pull the rest of the body after it. This guard:
///   1. <see cref="ResolveActivationPose"/>: before the bodies go dynamic, walks the bones
///      parent-first and pulls back any bone (or bone tip) that a line from a known-safe
///      point (the character's capsule centre, then each parent bone) can't reach without
///      crossing geometry.
///   2. <see cref="Step"/>: while simulating, reverts any body whose last physics step
///      crossed geometry (swept from its last valid pose, linked from its parent, or along its
///      own length) to that last valid pose.
/// Only static geometry counts (no Rigidbody, or a kinematic non-junk/non-ragdoll body such as a
/// door). Characters, NavMesh agents, gore and other ragdolls are ignored.
/// </summary>
public sealed class RagdollWallGuard
{
    private readonly Rigidbody[] _bodies;      // parent-before-child
    private readonly int[] _parentIndex;       // nearest ancestor ragdoll body, -1 for roots
    private readonly float[] _skin;            // pull-back distance (≈ bone radius)
    private readonly Collider[] _tipCollider;  // bone collider used for the tip test
    private readonly Vector3[] _lastPos;
    private readonly Quaternion[] _lastRot;
    private readonly HashSet<Collider> _own;
    private readonly Transform _ownerRoot;
    private readonly RaycastHit[] _hits = new RaycastHit[16];
    private readonly int _mask;

    private const float MinSkin = 0.04f;
    private const float ExtraSkin = 0.02f;

    public RagdollWallGuard(Transform ownerRoot, IReadOnlyList<Rigidbody> bodies)
    {
        var list = new List<Rigidbody>();
        foreach (Rigidbody rb in bodies)
            if (rb != null) list.Add(rb);
        list.Sort((a, b) => Depth(a.transform).CompareTo(Depth(b.transform)));

        int n = list.Count;
        _bodies = list.ToArray();
        _parentIndex = new int[n];
        _skin = new float[n];
        _tipCollider = new Collider[n];
        _lastPos = new Vector3[n];
        _lastRot = new Quaternion[n];

        var indexOf = new Dictionary<Transform, int>(n);
        for (int i = 0; i < n; i++) indexOf[_bodies[i].transform] = i;

        for (int i = 0; i < n; i++)
        {
            _parentIndex[i] = -1;
            for (Transform p = _bodies[i].transform.parent; p != null; p = p.parent)
            {
                if (indexOf.TryGetValue(p, out int pi)) { _parentIndex[i] = pi; break; }
                if (p == ownerRoot) break;
            }

            Collider col = _bodies[i].GetComponent<Collider>();
            _tipCollider[i] = col;
            _skin[i] = Mathf.Max(MinSkin, EstimateRadius(col)) + ExtraSkin;
        }

        _ownerRoot = ownerRoot;
        _own = new HashSet<Collider>(ownerRoot.GetComponentsInChildren<Collider>(true));

        int layer = n > 0 ? _bodies[0].gameObject.layer : 0;
        int mask = 0;
        for (int l = 0; l < 32; l++)
            if (!Physics.GetIgnoreLayerCollision(layer, l)) mask |= 1 << l;
        _mask = mask;
    }

    // ── Activation ───────────────────────────────────────────────────────────

    /// <summary>
    /// Call while the bodies are still kinematic and the Animator is off, BEFORE they go dynamic.
    /// <paramref name="safeAnchor"/> must be a point known to be in open space on the character's
    /// side of any wall (e.g. its CharacterController/NavMeshAgent capsule centre).
    /// Returns true if any bone was moved (caller should Physics.SyncTransforms()).
    /// </summary>
    public bool ResolveActivationPose(Vector3 safeAnchor)
    {
        // Re-collect every activation: attachments (held items, fitted corpse colliders) change.
        _own.Clear();
        if (_ownerRoot != null)
            _own.UnionWith(_ownerRoot.GetComponentsInChildren<Collider>(true));

        bool moved = false;
        for (int i = 0; i < _bodies.Length; i++)
        {
            Transform bone = _bodies[i].transform;
            Vector3 from = _parentIndex[i] >= 0 ? _bodies[_parentIndex[i]].transform.position : safeAnchor;

            // Pivot: reachable from the parent / anchor?
            if (TryGetBlocker(from, bone.position, out RaycastHit hit))
            {
                Vector3 back = (from - bone.position).normalized;
                float maxBack = Vector3.Distance(from, hit.point);
                Vector3 target = hit.point + back * Mathf.Min(_skin[i], maxBack);
                bone.position = target; // children follow through the hierarchy
                moved = true;
            }

            // Tip: does the bone's own length poke through?
            if (TryGetTip(i, out Vector3 tip) && TryGetBlocker(bone.position, tip, out hit))
            {
                Vector3 back = (bone.position - tip).normalized;
                bone.position += (hit.point - tip) + back * _skin[i];
                moved = true;
            }
        }
        return moved;
    }

    /// <summary>Records the current body poses as the last valid state. Call right after going dynamic.</summary>
    public void BeginTracking()
    {
        for (int i = 0; i < _bodies.Length; i++)
        {
            _lastPos[i] = _bodies[i].position;
            _lastRot[i] = _bodies[i].rotation;
        }
    }

    // ── Simulation ───────────────────────────────────────────────────────────

    /// <summary>Call from FixedUpdate while the ragdoll is simulating.</summary>
    public void Step()
    {
        for (int i = 0; i < _bodies.Length; i++)
        {
            Rigidbody rb = _bodies[i];
            if (rb == null || rb.isKinematic) continue;

            Vector3 pos = rb.position;
            bool crossed = TryGetBlocker(_lastPos[i], pos, out _);

            if (!crossed && _parentIndex[i] >= 0)
                crossed = TryGetBlocker(_bodies[_parentIndex[i]].position, pos, out _);

            if (!crossed && TryGetTip(i, out Vector3 tip))
                crossed = TryGetBlocker(pos, tip, out _);

            if (crossed)
            {
                rb.position = _lastPos[i];
                rb.rotation = _lastRot[i];
                rb.linearVelocity = Vector3.zero;
                rb.angularVelocity *= 0.25f;
            }
            else
            {
                _lastPos[i] = pos;
                _lastRot[i] = rb.rotation;
            }
        }
    }

    // ── Queries ──────────────────────────────────────────────────────────────

    private bool TryGetBlocker(Vector3 from, Vector3 to, out RaycastHit blocker)
    {
        blocker = default;
        Vector3 delta = to - from;
        float dist = delta.magnitude;
        if (dist < 0.001f) return false;

        int count = Physics.RaycastNonAlloc(from, delta / dist, _hits, dist, _mask, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        bool found = false;
        for (int h = 0; h < count; h++)
        {
            RaycastHit hit = _hits[h];
            if (hit.distance >= best || !IsWorldGeometry(hit.collider)) continue;
            best = hit.distance;
            blocker = hit;
            found = true;
        }
        return found;
    }

    private bool IsWorldGeometry(Collider col)
    {
        if (col == null || _own.Contains(col) || col is CharacterController) return false;

        Rigidbody body = col.attachedRigidbody;
        if (body != null)
        {
            if (!body.isKinematic) return false;
            if (body.GetComponentInParent<RagdollController>() != null) return false;
            if (body.GetComponentInParent<JunkItem>() != null) return false;
        }

        return col.GetComponentInParent<NavMeshAgent>() == null;
    }

    /// <summary>Far end of the bone's collider (relative to its pivot), for the "poking through" test.</summary>
    private bool TryGetTip(int i, out Vector3 tip)
    {
        tip = default;
        Collider col = _tipCollider[i];
        if (col == null) return false; // may still be disabled at activation — geometry is what matters

        Transform t = col.transform;
        Vector3 a, b;
        switch (col)
        {
            case CapsuleCollider c:
            {
                Vector3 axis = c.direction == 0 ? Vector3.right : c.direction == 2 ? Vector3.forward : Vector3.up;
                float half = Mathf.Max(0f, c.height * 0.5f - c.radius);
                a = t.TransformPoint(c.center + axis * half);
                b = t.TransformPoint(c.center - axis * half);
                break;
            }
            case BoxCollider bx:
            {
                Vector3 s = bx.size;
                Vector3 axis = s.x >= s.y && s.x >= s.z ? new Vector3(s.x * 0.5f, 0f, 0f)
                             : s.y >= s.z ? new Vector3(0f, s.y * 0.5f, 0f)
                             : new Vector3(0f, 0f, s.z * 0.5f);
                a = t.TransformPoint(bx.center + axis);
                b = t.TransformPoint(bx.center - axis);
                break;
            }
            case SphereCollider sp:
                a = b = t.TransformPoint(sp.center);
                break;
            default:
                return false;
        }

        Vector3 pivot = t.position;
        tip = (a - pivot).sqrMagnitude >= (b - pivot).sqrMagnitude ? a : b;
        return (tip - pivot).sqrMagnitude > 0.0001f;
    }

    private static float EstimateRadius(Collider col)
    {
        switch (col)
        {
            case CapsuleCollider c:
                return c.radius * MaxAbs(c.transform.lossyScale);
            case SphereCollider s:
                return s.radius * MaxAbs(s.transform.lossyScale);
            case BoxCollider b:
            {
                Vector3 e = Vector3.Scale(b.size, b.transform.lossyScale) * 0.5f;
                return Mathf.Min(Mathf.Abs(e.x), Mathf.Min(Mathf.Abs(e.y), Mathf.Abs(e.z)));
            }
            default:
                return MinSkin;
        }
    }

    private static float MaxAbs(Vector3 v) => Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z)));

    private static int Depth(Transform t)
    {
        int d = 0;
        for (Transform p = t.parent; p != null; p = p.parent) d++;
        return d;
    }
}
