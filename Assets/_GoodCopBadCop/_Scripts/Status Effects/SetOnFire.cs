
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
    public float spawnInterval = 0.3f;

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

        Animator anim = animator != null ? animator : GetComponentInChildren<Animator>();

        if (anim == null)
        {
            Debug.LogWarning($"[SetOnFire] No Animator found on {gameObject.name}.");
            return;
        }

        if (!anim.isHuman)
        {
            Debug.LogWarning($"[SetOnFire] Animator on {gameObject.name} is not Humanoid.");
            return;
        }

        if (firePrefab == null)
        {
            Debug.LogWarning($"[SetOnFire] No fire prefab assigned on {gameObject.name}.");
            return;
        }

        _isOnFire = true;

        // Collect all valid bone transforms
        var allBones = new List<Transform>();
        foreach (HumanBodyBones bone in System.Enum.GetValues(typeof(HumanBodyBones)))
        {
            if (bone == HumanBodyBones.LastBone) continue;
            Transform t = anim.GetBoneTransform(bone);
            if (t != null) allBones.Add(t);
        }

        // Shuffle and take one third
        Shuffle(allBones);
        int count = Mathf.Max(1, allBones.Count / 5);
        allBones = allBones.GetRange(0, count);

        StartCoroutine(SpawnFireOverTime(allBones));
    }

    private IEnumerator SpawnFireOverTime(List<Transform> bones)
    {
        foreach (Transform boneTransform in bones)
        {
            if (!_isOnFire) yield break;

            Vector3 worldPos = boneTransform.position;
            if (boneTransform.childCount > 0)
                worldPos = Vector3.Lerp(boneTransform.position, boneTransform.GetChild(0).position, 0.5f);

            GameObject instance = Instantiate(firePrefab, boneTransform);
            instance.transform.position = worldPos;
            instance.transform.localRotation = Quaternion.identity;
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