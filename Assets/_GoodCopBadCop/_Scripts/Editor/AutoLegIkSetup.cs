using UnityEngine;
using UnityEditor;
using UnityEngine.Animations.Rigging;

public class AutoLegIKSetup
{
    private const string controlLayerName = "IKControls";

    [MenuItem("Tools/Auto Setup Leg IK (URP Final)")]
    static void SetupLegIK()
    {
        GameObject root = Selection.activeGameObject;

        if (!root)
        {
            Debug.LogWarning("Select a character root.");
            return;
        }

        EnsureLayerExists();

        SetupSide(root, "Left");
        SetupSide(root, "Right");

        Debug.Log("Leg IK setup complete.");
    }

    static void SetupSide(GameObject root, string side)
    {
        Transform hip   = FindBone(root.transform, $"mixamorig:{side}UpLeg");
        Transform knee  = FindBone(root.transform, $"mixamorig:{side}Leg");
        Transform ankle = FindBone(root.transform, $"mixamorig:{side}Foot");

        if (!hip || !knee || !ankle)
        {
            Debug.LogWarning($"Could not find {side} Mixamo bones.");
            return;
        }

        // Add IK component
        SimpleLegIK_CCD ik = root.AddComponent<SimpleLegIK_CCD>();
        ik.hip = hip;
        ik.knee = knee;
        ik.ankle = ankle;

        // Create Target
        GameObject target = CreateControlSphere($"{side}_IK_Target", ankle.position, ankle.rotation, root.transform);
        ik.target = target.transform;

        // Create Pole
        GameObject pole = CreateControlSphere($"{side}_IK_Pole", knee.position + root.transform.forward * 0.5f, Quaternion.identity, root.transform);
        ik.pole = pole.transform;

        // Auto-add BoneRenderer
        AddHumanoidBoneRenderer(root);

        Undo.RegisterCreatedObjectUndo(target, "Create IK Target");
        Undo.RegisterCreatedObjectUndo(pole, "Create IK Pole");
    }

    static GameObject CreateControlSphere(string name, Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject obj = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        obj.name = name;

        obj.transform.position = position;
        obj.transform.rotation = rotation;
        obj.transform.localScale = Vector3.one * 0.2f;

        obj.transform.SetParent(parent, true);

        obj.layer = LayerMask.NameToLayer(controlLayerName);

        SphereCollider col = obj.GetComponent<SphereCollider>();
        col.isTrigger = true;

        ApplyURPTransparentMaterial(obj);

        // Prevent appearing in build
        obj.hideFlags = HideFlags.DontSaveInBuild;

        return obj;
    }

    static void ApplyURPTransparentMaterial(GameObject obj)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (!shader)
        {
            Debug.LogError("URP Lit shader not found.");
            return;
        }

        Material mat = new Material(shader);

        mat.SetFloat("_Surface", 1); // Transparent
        mat.SetFloat("_Blend", 0);
        mat.SetFloat("_ZWrite", 0);
        mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);

        mat.color = new Color(0.4f, 1f, 0.4f, 0.35f);

        mat.renderQueue = 5000; // Overlay queue

        obj.GetComponent<MeshRenderer>().sharedMaterial = mat;
    }


    static void AddHumanoidBoneRenderer(GameObject root)
    {
        Animator animator = root.GetComponentInChildren<Animator>();
        if (!animator || !animator.isHuman)
        {
            Debug.LogWarning("Animator not found or not Humanoid.");
            return;
        }

        BoneRenderer boneRenderer = root.GetComponent<BoneRenderer>();
        if (!boneRenderer)
            boneRenderer = root.AddComponent<BoneRenderer>();

        var bones = new System.Collections.Generic.List<Transform>();

        foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone)
                continue;

            Transform t = animator.GetBoneTransform(bone);
            if (t != null && !bones.Contains(t))
                bones.Add(t);
        }

        boneRenderer.transforms = bones.ToArray();
        EditorUtility.SetDirty(boneRenderer);
    }



    static Transform FindBone(Transform root, string exactName)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t.name == exactName)
                return t;
        }
        return null;
    }

    static void EnsureLayerExists()
    {
        SerializedObject tagManager = new SerializedObject(
            AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]
        );

        SerializedProperty layersProp = tagManager.FindProperty("layers");

        for (int i = 8; i < 32; i++)
        {
            SerializedProperty sp = layersProp.GetArrayElementAtIndex(i);
            if (sp.stringValue == controlLayerName)
                return;

            if (string.IsNullOrEmpty(sp.stringValue))
            {
                sp.stringValue = controlLayerName;
                tagManager.ApplyModifiedProperties();
                return;
            }
        }
    }
}
