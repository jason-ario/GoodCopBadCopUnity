using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Which collider shape to use for a ragdoll bone.
/// </summary>
public enum RagdollColliderShape { Capsule, Sphere }

/// <summary>
/// One bone that should become a physics ragdoll part. Add one entry per bone,
/// for any skeleton (humanoid, creature, multi-limbed mutant, etc). This does NOT
/// assume any fixed named bones (no "Hips"/"Head"/"LeftArm" requirement) - it works
/// purely off whichever Transforms you assign, so it generalizes to any body type.
/// </summary>
[System.Serializable]
public class RagdollBoneEntry
{
    [Tooltip("The bone this ragdoll part is built on.")]
    public Transform bone;

    [Tooltip("The bone this one connects to via a CharacterJoint. Leave empty for the single root bone (e.g. hips/pelvis) of the ragdoll.")]
    public Transform connectTo;

    [Tooltip("Rigidbody mass for this part.")]
    public float mass = 1f;

    [Tooltip("Collider shape. Capsule fits most limb/spine bones; Sphere suits heads or round parts.")]
    public RagdollColliderShape shape = RagdollColliderShape.Capsule;

    [Tooltip("Collider radius. Leave at 0 to auto-estimate from the distance to this bone's furthest child.")]
    public float radius = 0f;

    [Tooltip("CharacterJoint swing limit in degrees (how far the joint can bend sideways/forward).")]
    public float swingLimit = 40f;

    [Tooltip("CharacterJoint twist limit in degrees (how far the joint can rotate along the bone axis).")]
    public float twistLimit = 20f;
}

/// <summary>
/// Backing data for the Ragdoll Builder window. Kept as a ScriptableObject purely so
/// Unity's built-in property drawers / Undo work for the entries list in the window.
/// </summary>
public class RagdollBuilderData : ScriptableObject
{
    public List<RagdollBoneEntry> entries = new List<RagdollBoneEntry>();
}

/// <summary>
/// Static build/remove logic shared by the window. Adds Rigidbody + Collider (+
/// CharacterJoint for non-root bones) to each configured entry, tagging every part
/// with a RagdollBone marker component so it can be found and removed later without
/// touching unrelated colliders/rigidbodies on the character.
/// </summary>
public static class RagdollBuilderUtility
{
    public static void Build(List<RagdollBoneEntry> entries)
    {
        var rbMap = new Dictionary<Transform, Rigidbody>();

        foreach (var e in entries)
        {
            if (e.bone == null) continue;
            var go = e.bone.gameObject;

            var rb = go.GetComponent<Rigidbody>();
            if (rb == null) rb = Undo.AddComponent<Rigidbody>(go);
            rb.mass = e.mass;
            rbMap[e.bone] = rb;

            Vector3 tip = EstimateTip(e.bone);
            float dist = tip.magnitude;
            float radius = e.radius > 0f ? e.radius : Mathf.Max(dist * 0.3f, 0.01f);

            if (e.shape == RagdollColliderShape.Sphere || dist < 0.0001f)
            {
                var sc = Undo.AddComponent<SphereCollider>(go);
                sc.radius = radius;
                sc.center = tip * 0.5f;
            }
            else
            {
                var cc = Undo.AddComponent<CapsuleCollider>(go);
                cc.radius = radius;
                cc.height = Mathf.Max(dist, radius * 2f);
                cc.center = tip * 0.5f;
                cc.direction = DominantAxis(tip);
            }

            var marker = Undo.AddComponent<RagdollBone>(go);
            marker.isRoot = e.connectTo == null;
        }

        foreach (var e in entries)
        {
            if (e.bone == null || e.connectTo == null) continue;
            if (!rbMap.TryGetValue(e.connectTo, out var parentRb)) continue;

            var joint = Undo.AddComponent<CharacterJoint>(e.bone.gameObject);
            joint.connectedBody = parentRb;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.enableCollision = false;
            joint.swing1Limit = new SoftJointLimit { limit = e.swingLimit };
            joint.swing2Limit = new SoftJointLimit { limit = e.swingLimit };
            joint.lowTwistLimit = new SoftJointLimit { limit = -e.twistLimit };
            joint.highTwistLimit = new SoftJointLimit { limit = e.twistLimit };
        }
    }

    public static void Remove(Transform root)
    {
        var bones = root.GetComponentsInChildren<RagdollBone>(true);
        foreach (var b in bones)
        {
            var go = b.gameObject;
            var joint = go.GetComponent<CharacterJoint>();
            if (joint != null) Undo.DestroyObjectImmediate(joint);
            var cc = go.GetComponent<CapsuleCollider>();
            if (cc != null) Undo.DestroyObjectImmediate(cc);
            var sc = go.GetComponent<SphereCollider>();
            if (sc != null) Undo.DestroyObjectImmediate(sc);
            var rb = go.GetComponent<Rigidbody>();
            if (rb != null) Undo.DestroyObjectImmediate(rb);
            Undo.DestroyObjectImmediate(b);
        }
    }

    static Vector3 EstimateTip(Transform bone)
    {
        if (bone.childCount == 0) return Vector3.zero;
        Transform best = bone.GetChild(0);
        float bestDist = best.localPosition.sqrMagnitude;
        for (int i = 1; i < bone.childCount; i++)
        {
            var c = bone.GetChild(i);
            float d = c.localPosition.sqrMagnitude;
            if (d > bestDist) { best = c; bestDist = d; }
        }
        return best.localPosition;
    }

    static int DominantAxis(Vector3 v)
    {
        float ax = Mathf.Abs(v.x), ay = Mathf.Abs(v.y), az = Mathf.Abs(v.z);
        if (ax >= ay && ax >= az) return 0;
        if (ay >= ax && ay >= az) return 1;
        return 2;
    }
}

/// <summary>
/// Reusable editor tool: build or remove a ragdoll (Rigidbody + Collider +
/// CharacterJoint chain) for any character, regardless of skeleton/body type.
/// Unlike Unity's built-in Ragdoll Wizard, it does not require named bones
/// (Hips/Spine/Head/LeftArm/...) - just assign whichever bones should be
/// physical, and which bone each one connects to.
/// Open via Tools > Ragdoll Builder.
/// </summary>
public class RagdollBuilderWindow : EditorWindow
{
    RagdollBuilderData data;
    SerializedObject so;
    Vector2 scroll;
    Transform removeRoot;

    [MenuItem("Tools/Ragdoll Builder")]
    public static void Open()
    {
        GetWindow<RagdollBuilderWindow>("Ragdoll Builder");
    }

    void OnEnable()
    {
        data = ScriptableObject.CreateInstance<RagdollBuilderData>();
        data.hideFlags = HideFlags.DontSave;
        so = new SerializedObject(data);
    }

    void OnGUI()
    {
        so.Update();

        EditorGUILayout.HelpBox(
            "Works for any body type - humanoid, creature, or anything else.\n\n" +
            "1) Add one entry per bone that should become a physics ragdoll part.\n" +
            "2) For the single root bone (e.g. hips/pelvis) leave 'Connect To' empty.\n" +
            "3) For every other bone, set 'Connect To' to whichever bone it should joint to " +
            "(usually its nearest ragdolled ancestor - not necessarily its direct Transform parent).\n" +
            "4) Click Build Ragdoll.\n\n" +
            "To remove a ragdoll later, assign its root bone below and click Remove Ragdoll.",
            MessageType.Info);

        EditorGUILayout.Space();
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.PropertyField(so.FindProperty("entries"), true);
        EditorGUILayout.EndScrollView();
        so.ApplyModifiedProperties();

        EditorGUILayout.Space();
        if (GUILayout.Button("Build Ragdoll", GUILayout.Height(28)))
        {
            RagdollBuilderUtility.Build(data.entries);
            Debug.Log($"Ragdoll built for {data.entries.Count} bone(s).");
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Remove an existing ragdoll", EditorStyles.boldLabel);
        removeRoot = (Transform)EditorGUILayout.ObjectField("Ragdoll Root", removeRoot, typeof(Transform), true);
        using (new EditorGUI.DisabledScope(removeRoot == null))
        {
            if (GUILayout.Button("Remove Ragdoll", GUILayout.Height(28)))
            {
                RagdollBuilderUtility.Remove(removeRoot);
                Debug.Log($"Ragdoll removed under {removeRoot.name}.");
            }
        }
    }
}
