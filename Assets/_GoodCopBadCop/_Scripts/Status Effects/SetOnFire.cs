
using System.Collections;
using UnityEngine;
using System.Collections.Generic;

public class SetOnFire : MonoBehaviour
{
    [Tooltip("Animator to source bones from. Auto-detected if left empty.")]
    public Animator animator;

    [Tooltip("Fire prefab to spawn on each bone.")]
    public GameObject firePrefab;

    [Tooltip("How many seconds between each fire spawn.")]
    public float spawnInterval = 1.5f;

    [Tooltip("Maximum number of fire emitters that can be active on this character at once.")]
    public int maxFireInstances = 5;

    private readonly List<GameObject> _fireInstances = new List<GameObject>();
    private bool _isOnFire = false;

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    public void Ignite()
    {
        if (_isOnFire) return;

        if (firePrefab == null)
        {
            Debug.LogWarning($"[SetOnFire] No fire prefab assigned on {gameObject.name}.");
            return;
        }

        List<Transform> allBones = CollectBones();

        if (allBones.Count == 0)
        {
            Debug.LogWarning($"[SetOnFire] No bones found on {gameObject.name}.");
            return;
        }

        _isOnFire = true;

        Shuffle(allBones);
        int count = Mathf.Max(1, allBones.Count / 5);
        allBones = allBones.GetRange(0, count);

        StartCoroutine(SpawnFireOverTime(allBones));
    }

    /// <summary>
    /// Collects bone transforms for fire spawning.
    /// For humanoid rigs uses <see cref="HumanBodyBones"/> via the Animator.
    /// For generic rigs falls back to <see cref="SkinnedMeshRenderer.bones"/>.
    /// </summary>
    private List<Transform> CollectBones()
    {
        Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();

        if (anim != null && anim.isHuman)
        {
            var bones = new List<Transform>();
            foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
            {
                if (bone == HumanBodyBones.LastBone) continue;
                Transform t = anim.GetBoneTransform(bone);
                if (t != null) bones.Add(t);
            }
            return bones;
        }

        // Generic rig: read bone transforms directly from the SkinnedMeshRenderer.
        SkinnedMeshRenderer smr = GetComponentInChildren<SkinnedMeshRenderer>();
        if (smr != null && smr.bones.Length > 0)
            return new List<Transform>(smr.bones);

        return new List<Transform>();
    }

    private IEnumerator SpawnFireOverTime(List<Transform> bones)
    {
        foreach (Transform boneTransform in bones)
        {
            if (!_isOnFire) yield break;
            if (_fireInstances.Count >= maxFireInstances) yield break;

            Vector3 worldPos = boneTransform.position;
            if (boneTransform.childCount > 0)
                worldPos = Vector3.Lerp(boneTransform.position, boneTransform.GetChild(0).position, 0.5f);

            GameObject instance = Instantiate(firePrefab, boneTransform);
            instance.transform.position = worldPos;
            instance.transform.localRotation = Quaternion.identity;

            // Compensate for the bone's lossy scale so the fire renders at the
            // prefab's intended world size regardless of the character's scale.
            Vector3 prefabScale  = firePrefab.transform.localScale;
            Vector3 parentLossy  = boneTransform.lossyScale;
            instance.transform.localScale = new Vector3(
                parentLossy.x != 0f ? prefabScale.x / parentLossy.x : prefabScale.x,
                parentLossy.y != 0f ? prefabScale.y / parentLossy.y : prefabScale.y,
                parentLossy.z != 0f ? prefabScale.z / parentLossy.z : prefabScale.z
            );

            instance.name = $"Fire_{boneTransform.name}";
            _fireInstances.Add(instance);

            yield return new WaitForSeconds(spawnInterval);
        }

        Debug.Log($"[SetOnFire] {gameObject.name} is on fire! ({_fireInstances.Count} emitters)");
    }

    public void Extinguish()
    {
        if (!_isOnFire) return;
        _isOnFire = false;

        StopAllCoroutines();

        foreach (GameObject instance in _fireInstances)
        {
            if (instance != null)
                Destroy(instance);
        }

        _fireInstances.Clear();
        Debug.Log($"[SetOnFire] {gameObject.name} extinguished.");
    }

    private void Shuffle(List<Transform> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}