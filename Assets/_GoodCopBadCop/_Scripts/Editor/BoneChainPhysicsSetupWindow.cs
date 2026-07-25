using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor tool that auto-rigs simple linear bone chains (tails, chains, ponytails,
/// hanging cables, etc.) with Rigidbodies, Sphere/Capsule colliders and Joints,
/// instead of doing it bone-by-bone by hand.
/// </summary>
public class BoneChainPhysicsSetupWindow : EditorWindow
{
    private enum ColliderShape { Capsule, Sphere }
    private enum JointMode { ConfigurableSpring, CharacterJoint }

    [MenuItem("Tools/Physics/Bone Chain Physics Setup")]
    private static void Open()
    {
        GetWindow<BoneChainPhysicsSetupWindow>("Bone Chain Physics");
    }

    private readonly List<Transform> chainRoots = new List<Transform>();
    private string endBoneNameFilter = "";

    private ColliderShape colliderShape = ColliderShape.Capsule;
    private float startRadius = 0.05f;
    private float endRadius = 0.03f;

    private float mass = 1f;
    private float linearDamping = 0.5f;
    private float angularDamping = 1f;
    private bool useGravity = true;
    private CollisionDetectionMode collisionMode = CollisionDetectionMode.Discrete;
    private bool pinRootIfNoParentBody = true;

    private JointMode jointMode = JointMode.ConfigurableSpring;

    // CharacterJoint settings
    private float swingLimitDeg = 30f;
    private float twistLimitDeg = 15f;

    // ConfigurableJoint spring settings
    private float angularLimitDeg = 30f;
    private float angularSpring = 80f;
    private float angularDamper = 4f;

    private Vector2 scroll;

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);

        EditorGUILayout.LabelField("Chain Roots", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Add the first bone of each chain (tail root, chain link 1, ponytail root...). " +
            "Each chain is walked by following the FIRST child of each bone until a leaf is reached, " +
            "or until a bone name matches the optional end filter below.",
            MessageType.Info);

        for (int i = 0; i < chainRoots.Count; i++)
        {
            EditorGUILayout.BeginHorizontal();
            chainRoots[i] = (Transform)EditorGUILayout.ObjectField(chainRoots[i], typeof(Transform), true);
            if (GUILayout.Button("X", GUILayout.Width(22)))
            {
                chainRoots.RemoveAt(i);
                i--;
            }
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Add Selected Transforms"))
        {
            foreach (Transform t in Selection.transforms)
            {
                if (!chainRoots.Contains(t))
                    chainRoots.Add(t);
            }
        }
        if (GUILayout.Button("Clear List"))
        {
            chainRoots.Clear();
        }
        EditorGUILayout.EndHorizontal();

        endBoneNameFilter = EditorGUILayout.TextField("End Bone Name Contains", endBoneNameFilter);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Colliders", EditorStyles.boldLabel);
        colliderShape = (ColliderShape)EditorGUILayout.EnumPopup("Collider Shape", colliderShape);
        startRadius = EditorGUILayout.FloatField("Start Radius", startRadius);
        endRadius = EditorGUILayout.FloatField("End Radius", endRadius);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Rigidbody", EditorStyles.boldLabel);
        mass = EditorGUILayout.FloatField("Mass", mass);
        linearDamping = EditorGUILayout.FloatField("Linear Damping", linearDamping);
        angularDamping = EditorGUILayout.FloatField("Angular Damping", angularDamping);
        useGravity = EditorGUILayout.Toggle("Use Gravity", useGravity);
        collisionMode = (CollisionDetectionMode)EditorGUILayout.EnumPopup("Collision Detection", collisionMode);
        pinRootIfNoParentBody = EditorGUILayout.Toggle("Pin Root (Kinematic) If No Parent Body", pinRootIfNoParentBody);

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Joints", EditorStyles.boldLabel);
        jointMode = (JointMode)EditorGUILayout.EnumPopup("Joint Type", jointMode);

        if (jointMode == JointMode.CharacterJoint)
        {
            swingLimitDeg = EditorGUILayout.FloatField("Swing Limit (deg)", swingLimitDeg);
            twistLimitDeg = EditorGUILayout.FloatField("Twist Limit (deg)", twistLimitDeg);
        }
        else
        {
            angularLimitDeg = EditorGUILayout.FloatField("Angular Limit (deg)", angularLimitDeg);
            angularSpring = EditorGUILayout.FloatField("Angular Spring", angularSpring);
            angularDamper = EditorGUILayout.FloatField("Angular Damper", angularDamper);
        }

        EditorGUILayout.Space();
        EditorGUILayout.BeginHorizontal();
        if (GUILayout.Button("Build Chain Physics", GUILayout.Height(30)))
        {
            SetupChains();
        }
        if (GUILayout.Button("Clear Chain Physics", GUILayout.Height(30)))
        {
            ClearChains();
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndScrollView();
    }

    private List<Transform> CollectChain(Transform root)
    {
        var list = new List<Transform>();
        Transform current = root;
        while (current != null)
        {
            list.Add(current);

            if (!string.IsNullOrEmpty(endBoneNameFilter) && current.name.Contains(endBoneNameFilter))
                break;

            if (current.childCount == 0)
                break;

            current = current.GetChild(0);
        }
        return list;
    }

    private void SetupChains()
    {
        if (chainRoots.Count == 0)
        {
            Debug.LogWarning("Bone Chain Physics: no chain roots assigned.");
            return;
        }

        foreach (Transform root in chainRoots)
        {
            if (root == null)
                continue;

            List<Transform> bones = CollectChain(root);
            if (bones.Count == 0)
                continue;

            BuildChain(bones);
        }

        Debug.Log($"Bone Chain Physics: built {chainRoots.Count} chain(s).");
    }

    private void BuildChain(List<Transform> bones)
    {
        Rigidbody parentAnchor = null;
        if (bones[0].parent != null)
            parentAnchor = bones[0].parent.GetComponent<Rigidbody>();

        Rigidbody previousBody = null;

        for (int i = 0; i < bones.Count; i++)
        {
            Transform bone = bones[i];
            Undo.RegisterFullObjectHierarchyUndo(bone.gameObject, "Build Bone Chain Physics");

            Rigidbody rb = bone.GetComponent<Rigidbody>();
            if (rb == null)
                rb = Undo.AddComponent<Rigidbody>(bone.gameObject);

            rb.mass = mass;
            rb.linearDamping = linearDamping;
            rb.angularDamping = angularDamping;
            rb.useGravity = useGravity;
            rb.collisionDetectionMode = collisionMode;
            rb.isKinematic = false;

            float t = bones.Count > 1 ? (float)i / (bones.Count - 1) : 0f;
            float radius = Mathf.Lerp(startRadius, endRadius, t);

            Vector3 localDir = GetLocalBoneDirection(bones, i);

            AddCollider(bone, bones, i, radius, localDir);

            if (i == 0)
            {
                if (parentAnchor != null)
                {
                    AddJoint(bone, parentAnchor, localDir);
                }
                else if (pinRootIfNoParentBody)
                {
                    rb.isKinematic = true;
                }
            }
            else
            {
                AddJoint(bone, previousBody, localDir);
            }

            previousBody = rb;
        }
    }

    private Vector3 GetLocalBoneDirection(List<Transform> bones, int index)
    {
        Transform bone = bones[index];

        if (index < bones.Count - 1)
        {
            Vector3 worldDir = (bones[index + 1].position - bone.position);
            if (worldDir.sqrMagnitude > 0.0000001f)
                return bone.InverseTransformDirection(worldDir.normalized);
        }
        else if (index > 0)
        {
            // Leaf bone: reuse the direction coming from its parent bone.
            Vector3 worldDir = (bone.position - bones[index - 1].position);
            if (worldDir.sqrMagnitude > 0.0000001f)
                return bone.InverseTransformDirection(worldDir.normalized);
        }

        return Vector3.up;
    }

    private void AddCollider(Transform bone, List<Transform> bones, int index, float radius, Vector3 localDir)
    {
        // Remove pre-existing colliders added by this tool so re-running is idempotent.
        RemoveComponentsOfType<SphereCollider>(bone.gameObject);
        RemoveComponentsOfType<CapsuleCollider>(bone.gameObject);

        bool isLeaf = index >= bones.Count - 1;

        if (colliderShape == ColliderShape.Sphere || isLeaf)
        {
            SphereCollider sphere = Undo.AddComponent<SphereCollider>(bone.gameObject);
            sphere.radius = radius;
            sphere.center = Vector3.zero;
            return;
        }

        float distance = Vector3.Distance(bone.position, bones[index + 1].position);

        CapsuleCollider capsule = Undo.AddComponent<CapsuleCollider>(bone.gameObject);
        capsule.radius = radius;
        capsule.height = Mathf.Max(distance, radius * 2f);
        capsule.center = localDir * (distance * 0.5f);
        capsule.direction = GetDominantAxis(localDir);
    }

    private static int GetDominantAxis(Vector3 localDir)
    {
        float ax = Mathf.Abs(localDir.x);
        float ay = Mathf.Abs(localDir.y);
        float az = Mathf.Abs(localDir.z);

        if (ax >= ay && ax >= az) return 0; // X
        if (ay >= ax && ay >= az) return 1; // Y
        return 2; // Z
    }

    private void AddJoint(Transform bone, Rigidbody connectedBody, Vector3 localDir)
    {
        RemoveComponentsOfType<CharacterJoint>(bone.gameObject);
        RemoveComponentsOfType<ConfigurableJoint>(bone.gameObject);

        Vector3 swingAxis = Vector3.Cross(localDir, Vector3.up);
        if (swingAxis.sqrMagnitude < 0.0001f)
            swingAxis = Vector3.Cross(localDir, Vector3.right);
        swingAxis.Normalize();

        if (jointMode == JointMode.CharacterJoint)
        {
            CharacterJoint joint = Undo.AddComponent<CharacterJoint>(bone.gameObject);
            joint.connectedBody = connectedBody;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.axis = localDir;
            joint.swingAxis = swingAxis;
            joint.enableProjection = true;
            joint.projectionDistance = 0.01f;
            joint.projectionAngle = 5f;

            SoftJointLimit twistLimit = new SoftJointLimit { limit = twistLimitDeg };
            joint.lowTwistLimit = new SoftJointLimit { limit = -twistLimitDeg };
            joint.highTwistLimit = twistLimit;

            SoftJointLimit swingLimit = new SoftJointLimit { limit = swingLimitDeg };
            joint.swing1Limit = swingLimit;
            joint.swing2Limit = swingLimit;
        }
        else
        {
            ConfigurableJoint joint = Undo.AddComponent<ConfigurableJoint>(bone.gameObject);
            joint.connectedBody = connectedBody;
            joint.anchor = Vector3.zero;
            joint.autoConfigureConnectedAnchor = true;
            joint.axis = localDir;
            joint.secondaryAxis = swingAxis;

            joint.xMotion = ConfigurableJointMotion.Locked;
            joint.yMotion = ConfigurableJointMotion.Locked;
            joint.zMotion = ConfigurableJointMotion.Locked;

            joint.angularXMotion = ConfigurableJointMotion.Limited;
            joint.angularYMotion = ConfigurableJointMotion.Limited;
            joint.angularZMotion = ConfigurableJointMotion.Limited;

            SoftJointLimit angularLimit = new SoftJointLimit { limit = angularLimitDeg };
            joint.lowAngularXLimit = new SoftJointLimit { limit = -angularLimitDeg };
            joint.highAngularXLimit = angularLimit;
            joint.angularYLimit = angularLimit;
            joint.angularZLimit = angularLimit;

            JointDrive drive = new JointDrive
            {
                positionSpring = angularSpring,
                positionDamper = angularDamper,
                maximumForce = Mathf.Infinity
            };

            joint.rotationDriveMode = RotationDriveMode.Slerp;
            joint.slerpDrive = drive;
            joint.targetRotation = Quaternion.identity;

            joint.projectionMode = JointProjectionMode.PositionAndRotation;
            joint.projectionDistance = 0.01f;
            joint.projectionAngle = 5f;
        }
    }

    private void ClearChains()
    {
        if (chainRoots.Count == 0)
        {
            Debug.LogWarning("Bone Chain Physics: no chain roots assigned.");
            return;
        }

        foreach (Transform root in chainRoots)
        {
            if (root == null)
                continue;

            List<Transform> bones = CollectChain(root);
            foreach (Transform bone in bones)
            {
                Undo.RegisterFullObjectHierarchyUndo(bone.gameObject, "Clear Bone Chain Physics");
                RemoveComponentsOfType<CharacterJoint>(bone.gameObject);
                RemoveComponentsOfType<ConfigurableJoint>(bone.gameObject);
                RemoveComponentsOfType<SphereCollider>(bone.gameObject);
                RemoveComponentsOfType<CapsuleCollider>(bone.gameObject);
                RemoveComponentsOfType<Rigidbody>(bone.gameObject);
            }
        }

        Debug.Log($"Bone Chain Physics: cleared {chainRoots.Count} chain(s).");
    }

    private static void RemoveComponentsOfType<T>(GameObject go) where T : Component
    {
        T[] components = go.GetComponents<T>();
        for (int i = 0; i < components.Length; i++)
        {
            Undo.DestroyObjectImmediate(components[i]);
        }
    }
}
