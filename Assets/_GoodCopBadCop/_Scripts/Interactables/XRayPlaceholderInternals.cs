using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// PLACEHOLDER content for the X-ray machine: builds a primitive skeleton (skull, spine, ribs,
/// pelvis, limb bones) and coloured organs (brain, lungs, heart, liver, stomach, kidneys,
/// intestines) on every active humanoid <see cref="Animator"/> under this suspect.
/// Parts are parented to the rig's bones so they follow animation, sized from the rig's
/// hips-to-head distance (suspect rigs vary wildly in scale), placed on the XRaySkeleton layer
/// with no materials, so only the X-ray feed draws them.
///
/// Added automatically by <see cref="XRayCamera"/> in Play Mode. Rigs that already contain
/// authored <see cref="XRayInternalPart"/>s are skipped, so real art can replace this per suspect.
/// </summary>
[DisallowMultipleComponent]
public class XRayPlaceholderInternals : MonoBehaviour
{
    private const string RootName = "XRay Placeholder Internals";

    private static Mesh s_sphere;
    private static Mesh s_capsule;

    private readonly HashSet<Animator> _built = new HashSet<Animator>();
    private readonly List<GameObject> _roots = new List<GameObject>();
    private readonly List<Animator> _animatorBuffer = new List<Animator>();

    // Per-build context.
    private int _layer;
    private float _h;
    private Vector3 _up, _fwd, _right;
    private Transform _currentRoot;

    /// <summary>Builds internals for any active humanoid rig that doesn't have them yet. Cheap to call repeatedly.</summary>
    public void EnsureBuilt()
    {
        GetComponentsInChildren(false, _animatorBuffer);
        foreach (var anim in _animatorBuffer)
        {
            if (anim == null || !anim.isHuman || _built.Contains(anim)) continue;
            _built.Add(anim);

            // Authored internals take priority over placeholders.
            if (anim.GetComponentInChildren<XRayInternalPart>(true) != null) continue;
            Build(anim);
        }
        _animatorBuffer.Clear();
    }

    /// <summary>Destroys every placeholder built by this component.</summary>
    public void Clear()
    {
        foreach (var root in _roots)
        {
            if (root == null) continue;
            if (Application.isPlaying) Destroy(root);
            else DestroyImmediate(root);
        }
        _roots.Clear();
        _built.Clear();
    }

    private void OnDestroy() => Clear();

    private void Build(Animator anim)
    {
        Transform hips = anim.GetBoneTransform(HumanBodyBones.Hips);
        Transform spine = anim.GetBoneTransform(HumanBodyBones.Spine);
        Transform chest = anim.GetBoneTransform(HumanBodyBones.Chest);
        Transform upperChest = anim.GetBoneTransform(HumanBodyBones.UpperChest);
        Transform neck = anim.GetBoneTransform(HumanBodyBones.Neck);
        Transform head = anim.GetBoneTransform(HumanBodyBones.Head);
        if (hips == null || head == null) return;

        if (spine == null) spine = hips;
        if (chest == null) chest = spine;
        if (upperChest == null) upperChest = chest;
        if (neck == null) neck = head;

        EnsureMeshes();
        _layer = LayerMask.NameToLayer(XRayCamera.SkeletonLayer);
        if (_layer < 0) _layer = 0;

        // Character-space axes: up along the trunk, forward = facing.
        Vector3 trunk = neck.position - hips.position;
        _h = Vector3.Distance(hips.position, head.position);
        if (_h < 1e-4f) return;
        _up = trunk.normalized;
        _fwd = Vector3.ProjectOnPlane(anim.transform.forward, _up).normalized;
        if (_fwd.sqrMagnitude < 1e-4f) _fwd = Vector3.ProjectOnPlane(Vector3.forward, _up).normalized;
        _right = Vector3.Cross(_up, _fwd);

        var rootGO = new GameObject(RootName) { layer = _layer };
        rootGO.transform.SetParent(anim.transform, false);
        _roots.Add(rootGO);
        _currentRoot = rootGO.transform;

        Vector3 Trunk(float t) => hips.position + trunk * t;
        Transform TrunkBone(float t) => t < 0.3f ? hips : t < 0.5f ? spine : t < 0.72f ? chest : upperChest;
        float h = _h;

        // Torso width unit from the shoulder span, so wide/stocky suspects get wider organs and ribs.
        // A typical shoulder span (upper-arm joints) is ~0.41 x hips-to-head.
        Transform lArm = anim.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        Transform rArm = anim.GetBoneTransform(HumanBodyBones.RightUpperArm);
        float w = (lArm != null && rArm != null) ? Vector3.Distance(lArm.position, rArm.position) / 0.41f : h;
        w = Mathf.Clamp(w, h * 0.7f, h * 2f);
        Vector3 Torso(float x, float y, float z) => new Vector3(x * w, y * h, z * w);
        Vector3 Offset(float t, float x, float z) => Trunk(t) + _right * x * w + _fwd * z * w;

        // ── Skeleton ────────────────────────────────────────────────────────
        var bone = XRayInternalPart.PartKind.Skeleton;
        Color white = Color.white;

        Sphere("Skull", head, head.position + _up * 0.15f * h + _fwd * 0.02f * h, new Vector3(0.2f, 0.24f, 0.23f) * h, bone, white);
        Bone("Spine Lower", hips, hips.position, spine.position, 0.045f * h);
        Bone("Spine Mid", spine, spine.position, chest.position, 0.045f * h);
        Bone("Spine Upper", chest, chest.position, neck.position, 0.045f * h);
        Bone("Neck", neck, neck.position, head.position + _up * 0.03f * h, 0.035f * h);
        Sphere("Pelvis", hips, hips.position - _up * 0.02f * h, Torso(0.3f, 0.11f, 0.17f), bone, white);

        for (int i = 0; i < 5; i++)
        {
            float t = Mathf.Lerp(0.5f, 0.9f, i / 4f);
            float width = Mathf.Lerp(0.34f, 0.27f, Mathf.Abs(i - 2f) / 2f);
            Sphere($"Rib {i + 1}", TrunkBone(t), Offset(t, 0f, 0.02f),
                   Torso(width, 0.008f, width * 0.72f), bone, white);
        }

        BuildLimbs(anim, upperChest, h);

        // ── Organs ──────────────────────────────────────────────────────────
        var organ = XRayInternalPart.PartKind.Organ;
        Sphere("Brain", head, head.position + _up * 0.16f * h + _fwd * 0.02f * h, new Vector3(0.17f, 0.14f, 0.19f) * h, organ, new Color(1f, 0.42f, 0.6f));
        Sphere("Lung L", TrunkBone(0.76f), Offset(0.76f, -0.085f, -0.01f), Torso(0.13f, 0.22f, 0.13f), organ, new Color(0.95f, 0.28f, 0.34f));
        Sphere("Lung R", TrunkBone(0.76f), Offset(0.76f, 0.085f, -0.01f), Torso(0.13f, 0.22f, 0.13f), organ, new Color(0.95f, 0.28f, 0.34f));
        Sphere("Heart", TrunkBone(0.7f), Offset(0.7f, -0.03f, 0.075f), Torso(0.1f, 0.11f, 0.09f), organ, new Color(0.9f, 0.02f, 0.05f));
        Sphere("Liver", TrunkBone(0.52f), Offset(0.52f, 0.065f, 0.04f), Torso(0.2f, 0.1f, 0.13f), organ, new Color(0.5f, 0.08f, 0.05f));
        Sphere("Stomach", TrunkBone(0.5f), Offset(0.5f, -0.075f, 0.055f), Torso(0.13f, 0.1f, 0.1f), organ, new Color(1f, 0.62f, 0.12f));
        Sphere("Kidney L", TrunkBone(0.4f), Offset(0.4f, -0.07f, -0.05f), Torso(0.06f, 0.09f, 0.06f), organ, new Color(0.55f, 0.08f, 0.3f));
        Sphere("Kidney R", TrunkBone(0.4f), Offset(0.4f, 0.07f, -0.05f), Torso(0.06f, 0.09f, 0.06f), organ, new Color(0.55f, 0.08f, 0.3f));
        Sphere("Intestines", TrunkBone(0.24f), Offset(0.24f, 0f, 0.05f), Torso(0.24f, 0.16f, 0.14f), organ, new Color(0.95f, 0.42f, 0.25f));

        _currentRoot = null;
    }

    private void BuildLimbs(Animator anim, Transform upperChest, float h)
    {
        Transform B(HumanBodyBones b) => anim.GetBoneTransform(b);

        Chain("L", upperChest, B(HumanBodyBones.LeftUpperArm), B(HumanBodyBones.LeftLowerArm), B(HumanBodyBones.LeftHand), 0.04f * h, 0.03f * h);
        Chain("R", upperChest, B(HumanBodyBones.RightUpperArm), B(HumanBodyBones.RightLowerArm), B(HumanBodyBones.RightHand), 0.04f * h, 0.03f * h);
        Leg("L", B(HumanBodyBones.LeftUpperLeg), B(HumanBodyBones.LeftLowerLeg), B(HumanBodyBones.LeftFoot), B(HumanBodyBones.LeftToes), h);
        Leg("R", B(HumanBodyBones.RightUpperLeg), B(HumanBodyBones.RightLowerLeg), B(HumanBodyBones.RightFoot), B(HumanBodyBones.RightToes), h);
    }

    private void Chain(string side, Transform upperChest, Transform upper, Transform lower, Transform hand, float upperThickness, float lowerThickness)
    {
        if (upper == null || lower == null) return;
        Bone($"Clavicle {side}", upperChest, upperChest.position + _fwd * 0.03f * _h, upper.position, 0.025f * _h);
        Bone($"Humerus {side}", upper, upper.position, lower.position, upperThickness);
        if (hand == null) return;
        Bone($"Forearm {side}", lower, lower.position, hand.position, lowerThickness);
        Vector3 handDir = (hand.position - lower.position).normalized;
        Bone($"Hand {side}", hand, hand.position, hand.position + handDir * 0.1f * _h, 0.035f * _h);
    }

    private void Leg(string side, Transform upper, Transform lower, Transform foot, Transform toes, float h)
    {
        if (upper == null || lower == null) return;
        Bone($"Femur {side}", upper, upper.position, lower.position, 0.055f * h);
        if (foot == null) return;
        Bone($"Shin {side}", lower, lower.position, foot.position, 0.045f * h);
        Vector3 toe = toes != null ? toes.position : foot.position + _fwd * 0.15f * h;
        Bone($"Foot {side}", foot, foot.position, toe, 0.04f * h);
    }

    // ── Part factories ──────────────────────────────────────────────────────

    private void Bone(string name, Transform parent, Vector3 a, Vector3 b, float thickness)
    {
        Vector3 d = b - a;
        float len = d.magnitude;
        if (len < 1e-4f) return;
        var t = CreatePart(name, parent, s_capsule, XRayInternalPart.PartKind.Skeleton, Color.white);
        t.SetPositionAndRotation((a + b) * 0.5f, Quaternion.FromToRotation(Vector3.up, d / len));
        // Capsule primitive is 2 units tall and 1 unit wide.
        SetWorldScale(t, new Vector3(thickness, Mathf.Max(len * 0.5f, thickness * 0.5f), thickness));
    }

    private void Sphere(string name, Transform parent, Vector3 center, Vector3 size, XRayInternalPart.PartKind kind, Color color)
    {
        var t = CreatePart(name, parent, s_sphere, kind, color);
        t.SetPositionAndRotation(center, Quaternion.LookRotation(_fwd, _up));
        // Sphere primitive is 1 unit wide; size is (right, up, forward) in character space.
        SetWorldScale(t, size);
    }

    private Transform CreatePart(string name, Transform parent, Mesh mesh, XRayInternalPart.PartKind kind, Color color)
    {
        var go = new GameObject(name) { layer = _layer };
        go.transform.SetParent(parent != null ? parent : _currentRoot, true);

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        var mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterials = System.Array.Empty<Material>(); // drawn only by XRayBodyFeature
        mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        mr.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        mr.allowOcclusionWhenDynamic = false;

        go.AddComponent<XRayInternalPart>().Configure(kind, color);

        // Parts live under rig bones; track them via a root so Clear() can find them.
        if (_currentRoot != null && !_roots.Contains(go)) _roots.Add(go);
        return go.transform;
    }

    /// <summary>Sets a world-space scale on a child of a (uniformly scaled) bone.</summary>
    private static void SetWorldScale(Transform t, Vector3 worldScale)
    {
        Vector3 parentScale = t.parent != null ? t.parent.lossyScale : Vector3.one;
        t.localScale = new Vector3(
            worldScale.x / Mathf.Max(Mathf.Abs(parentScale.x), 1e-6f),
            worldScale.y / Mathf.Max(Mathf.Abs(parentScale.y), 1e-6f),
            worldScale.z / Mathf.Max(Mathf.Abs(parentScale.z), 1e-6f));
    }

    private static void EnsureMeshes()
    {
        if (s_sphere == null) s_sphere = GrabPrimitiveMesh(PrimitiveType.Sphere);
        if (s_capsule == null) s_capsule = GrabPrimitiveMesh(PrimitiveType.Capsule);
    }

    private static Mesh GrabPrimitiveMesh(PrimitiveType type)
    {
        var temp = GameObject.CreatePrimitive(type);
        if (temp.TryGetComponent(out Collider c)) c.enabled = false;
        Mesh mesh = temp.GetComponent<MeshFilter>().sharedMesh;
        if (Application.isPlaying) Destroy(temp);
        else DestroyImmediate(temp);
        return mesh;
    }
}
