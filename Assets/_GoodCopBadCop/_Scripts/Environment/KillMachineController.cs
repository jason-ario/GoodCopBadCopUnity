using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = System.Random;

public class KillMachineController : MonoBehaviour
{
    public static KillMachineController Instance;

    /// <summary>Raised on the server when the kill sequence finishes and the shield lowers.</summary>
    public event Action OnKillComplete;
    
    [SerializeField] private GameObject guns;
    [SerializeField] private GameObject killShield;
    [SerializeField] private AudioSource shootSFX;
    [SerializeField] private AudioSource windowSound;
    [SerializeField] AudioClip windowOpenSound;
    [SerializeField] AudioClip windowCloseSound;
    [SerializeField] BoxCollider boxCollider;
    [SerializeField] private GameObject[] bloodSplatters;
    [SerializeField] private GameObject[] bloodParticles;

    [Header("Body Hit Particles")]
    [Tooltip("Particle spawned at random points on the suspect's body while the guns fire (e.g. Head_Explosion).")]
    [SerializeField] private GameObject bodyHitParticlePrefab;
    [Tooltip("Seconds after the guns appear before body hits start (matches the shoot SFX start).")]
    [Min(0f)]
    [SerializeField] private float bodyHitStartDelay = 0.5f;
    [Tooltip("How long body hits keep spawning, in seconds (matches the firing window).")]
    [Min(0f)]
    [SerializeField] private float bodyHitDuration = 3f;
    [Tooltip("Random delay range between consecutive body hits.")]
    [SerializeField] private Vector2 bodyHitInterval = new Vector2(0.06f, 0.15f);
    [Tooltip("Random uniform scale range applied to each spawned body hit.")]
    [SerializeField] private Vector2 bodyHitScale = new Vector2(0.6f, 1f);
    [Tooltip("Random positional jitter (meters) around the chosen body point.")]
    [Min(0f)]
    [SerializeField] private float bodyHitJitter = 0.08f;

    /// <summary>Humanoid bones eligible as body-hit spawn points (fingers/toes excluded).</summary>
    private static readonly HumanBodyBones[] BodyHitBones =
    {
        HumanBodyBones.Head, HumanBodyBones.Neck,
        HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Spine, HumanBodyBones.Hips,
        HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm,
        HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg,
        HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
    };

    private void Awake()
    {
        Instance = this;
    }

    void OnDisable()
    { 
        ResetBloodSplatter();
    }

    public void ResetBloodSplatter()
    {
        for (int i = 0; i < bloodSplatters.Length - 1; i++)
        {
            bloodSplatters[i].SetActive(false);
        }
    }

    public void Kill()
    {
        StartCoroutine(KillSequence());
    }

    IEnumerator KillSequence()
    {
        killShield.SetActive(true);
        windowSound.PlayOneShot(windowCloseSound);

        yield return new WaitForSeconds(1f);
        guns.SetActive(true);

        SpawnBloodDecals();
        yield return new WaitForSeconds(.5f);
        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.GetComponent<PlayerCameraController>().TurnOnRumble();
        }
        shootSFX.Play();
        yield return new WaitForSeconds(1.5f);
        if (PlayerInstance.Instance != null)
        {
            PlayerInstance.Instance.GetComponent<PlayerCameraController>().TurnOffRumble();
        }
        yield return new WaitForSeconds(1.5f);
        shootSFX.Stop();
        windowSound.PlayOneShot(windowOpenSound);
        yield return new WaitForSeconds(2f);
        killShield.SetActive(false);
        OnKillComplete?.Invoke();
    }

    [ContextMenu("Spawn Blood Decals")]
    public void SpawnBloodDecals()
    {
        if (SuspectController.Instance != null && !SuspectController.Instance.HasEntityAtWindow)
            return;

        StartCoroutine(SpawnRandomBloodDecals());
        StartCoroutine(SpawnBloodParticles());
        StartCoroutine(SpawnBodyHitParticles());
    }

    /// <summary>
    /// Spawns <see cref="bodyHitParticlePrefab"/> at random points on the current suspect's body
    /// for the duration of the firing window. Purely cosmetic and local — this sequence already
    /// runs on every client, so each client rolls its own random hit points.
    /// </summary>
    IEnumerator SpawnBodyHitParticles()
    {
        if (bodyHitParticlePrefab == null)
            yield break;

        yield return new WaitForSeconds(bodyHitStartDelay);

        SuspectCharacter suspect = SuspectController.Instance != null ? SuspectController.Instance.CurrentSuspect : null;
        List<Transform> bones = CollectBodyHitBones(suspect);
        Renderer[] renderers = suspect != null ? suspect.GetComponentsInChildren<Renderer>() : Array.Empty<Renderer>();

        float elapsed = 0f;
        while (elapsed < bodyHitDuration)
        {
            Vector3 point = GetRandomBodyPoint(bones, renderers) + UnityEngine.Random.insideUnitSphere * bodyHitJitter;
            GameObject fx = Instantiate(bodyHitParticlePrefab, point, UnityEngine.Random.rotation);
            fx.transform.localScale *= UnityEngine.Random.Range(bodyHitScale.x, bodyHitScale.y);
            if (fx.GetComponentInChildren<AutoDestroy>() == null)
                fx.AddComponent<AutoDestroy>();

            float wait = UnityEngine.Random.Range(bodyHitInterval.x, bodyHitInterval.y);
            elapsed += wait;
            yield return new WaitForSeconds(wait);
        }
    }

    /// <summary>Returns the humanoid body bones of the suspect, or an empty list for non-humanoid rigs.</summary>
    private static List<Transform> CollectBodyHitBones(SuspectCharacter suspect)
    {
        var bones = new List<Transform>();
        Animator anim = suspect != null ? suspect.animator : null;
        if (anim == null || !anim.isHuman)
            return bones;

        foreach (HumanBodyBones bone in BodyHitBones)
        {
            Transform t = anim.GetBoneTransform(bone);
            if (t != null)
                bones.Add(t);
        }
        return bones;
    }

    /// <summary>
    /// Picks a live body bone position (tracks the death animation); falls back to a random point
    /// inside the suspect's renderer bounds, then inside the booth box collider.
    /// </summary>
    private Vector3 GetRandomBodyPoint(List<Transform> bones, Renderer[] renderers)
    {
        bones.RemoveAll(b => b == null);
        if (bones.Count > 0)
            return bones[UnityEngine.Random.Range(0, bones.Count)].position;

        Bounds bounds = default;
        bool hasBounds = false;
        foreach (Renderer r in renderers)
        {
            if (r == null || !r.enabled || r is ParticleSystemRenderer)
                continue;
            if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
            else bounds.Encapsulate(r.bounds);
        }

        if (!hasBounds)
            bounds = boxCollider.bounds;

        return new Vector3(
            UnityEngine.Random.Range(bounds.min.x, bounds.max.x),
            UnityEngine.Random.Range(bounds.min.y, bounds.max.y),
            UnityEngine.Random.Range(bounds.min.z, bounds.max.z));
    }

    IEnumerator SpawnBloodParticles()
    {
        yield return new WaitForSeconds(.5f);
        int bloodParticles = UnityEngine.Random.Range(5, 10);
        for (int i = 0; i < bloodParticles; i++)
        {
            Instantiate(this.bloodParticles[UnityEngine.Random.Range(0, this.bloodParticles.Length)], boxCollider.bounds.center, UnityEngine.Random.rotation);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.2f, 0.3f));
        }
    }
    IEnumerator SpawnRandomBloodDecals()
    {
        yield return new WaitForSeconds(.5f); 
        //Spawn Decals within box collider
        
        for (int i = 0; i < bloodSplatters.Length - 1; i++)
        {
            bloodSplatters[i].SetActive(true);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.1f, 0.2f));
        }
    }
}
