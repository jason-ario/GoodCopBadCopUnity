
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

    [Tooltip("Seconds after the last ignition call before the fire is automatically extinguished.")]
    public float burnDuration = 5f;

    [Tooltip("Damage dealt to the MutantEnemy every 0.5 seconds while on fire.")]
    public float fireDamagePerTick = 5f;

    private readonly List<GameObject> _fireInstances = new List<GameObject>();
    private bool _isOnFire = false;
    private bool _isMonitoring = false;
    private bool _isDamaging = false;
    private Coroutine _autoExtinguishCoroutine;

    /// <summary>True while at least one fire emitter is alive on this character.</summary>
    public bool IsOnFire => _isOnFire;

    /// <summary>True when the number of live (non-destroyed) emitters has reached <see cref="maxFireInstances"/>.</summary>
    public bool IsAtMaxFire => LiveFireCount() >= maxFireInstances;

    /// <summary>Counts only non-destroyed entries so the cap responds immediately when particles die.</summary>
    private int LiveFireCount()
    {
        int n = 0;
        foreach (GameObject f in _fireInstances)
            if (f != null) n++;
        return n;
    }

    void Start()
    {
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    /// <summary>
    /// Reassigns the Animator used for bone collection. Call this when the character's active
    /// skeleton changes at runtime (e.g. <see cref="SuspectCharacter"/> swapping from its civilian
    /// mesh to its Mutated Version mesh), so <see cref="Ignite"/> spawns fire on the correct bones.
    /// </summary>
    public void SetAnimator(Animator a) => animator = a;

    public void Ignite()
    {
        if (LiveFireCount() >= maxFireInstances) return;

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

        // Only start one monitor coroutine — it keeps running until all fires die.
        if (!_isMonitoring)
            StartCoroutine(MonitorFireInstances());

        if (!_isDamaging)
            StartCoroutine(DamageOverTime());

        // Reset the auto-extinguish timer so sustained burning keeps the fire alive.
        if (_autoExtinguishCoroutine != null)
            StopCoroutine(_autoExtinguishCoroutine);
        _autoExtinguishCoroutine = StartCoroutine(AutoExtinguish());
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
            if (LiveFireCount() >= maxFireInstances) yield break;

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

    /// <summary>
    /// Polls the live instance list every half-second. Removes destroyed entries and
    /// resets <see cref="IsOnFire"/> once all emitters have burned out, allowing the
    /// character to be ignited again.
    /// </summary>
    private IEnumerator MonitorFireInstances()
    {
        _isMonitoring = true;
        while (_isOnFire)
        {
            yield return new WaitForSeconds(0.5f);
            _fireInstances.RemoveAll(f => f == null);
            if (_fireInstances.Count == 0)
                _isOnFire = false;
        }
        _isMonitoring = false;
    }

    /// <summary>
    /// Ticks damage onto the <see cref="MutantEnemy"/> every 0.5 seconds while on fire.
    /// Only executes on the server so health changes are authoritative.
    /// </summary>
    private IEnumerator DamageOverTime()
    {
        _isDamaging = true;
        MutantEnemy enemy = GetComponent<MutantEnemy>();

        while (_isOnFire)
        {
            yield return new WaitForSeconds(0.5f);
            if (!_isOnFire) break;

            if (enemy != null && enemy.IsServer)
                enemy.TakeDamage(fireDamagePerTick, transform.position, isFireDamage: true);
        }

        _isDamaging = false;
    }

    private IEnumerator AutoExtinguish()
    {
        yield return new WaitForSeconds(burnDuration);
        Extinguish();
    }

    public void Extinguish()
    {
        if (!_isOnFire) return;
        _isOnFire = false;
        _isMonitoring = false;
        _isDamaging = false;
        _autoExtinguishCoroutine = null;

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