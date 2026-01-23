using System;
using System.Collections;
using UnityEngine;
using Random = System.Random;

public class KillMachineController : MonoBehaviour
{
    public static KillMachineController Instance;
    
    [SerializeField] private GameObject guns;
    [SerializeField] private GameObject killShield;
    [SerializeField] private AudioSource shootSFX;
    [SerializeField] private AudioSource windowSound;
    [SerializeField] AudioClip windowOpenSound;
    [SerializeField] AudioClip windowCloseSound;
    [SerializeField] BoxCollider boxCollider;
    [SerializeField] private GameObject[] bloodSplatters;
    [SerializeField] private GameObject[] bloodParticles;

    private void Awake()
    {
        Instance = this;
    }

    public void Kill()
    {
        StartCoroutine(KillSequence());
    }

    IEnumerator KillSequence()
    {
        killShield.SetActive(true);
        windowSound.PlayOneShot(windowCloseSound);
        yield return new WaitForSeconds(2f);
        SpawnBloodDecals();
        guns.SetActive(true);
        yield return new WaitForSeconds(.5f);
        PlayerInstance.Instance.GetComponent<PlayerCameraController>().TurnOnRumble();
        shootSFX.Play();
        yield return new WaitForSeconds(2.2f);
        PlayerInstance.Instance.GetComponent<PlayerCameraController>().TurnOffRumble();
        yield return new WaitForSeconds(3f);
        shootSFX.Stop();
        guns.SetActive(false);
        windowSound.PlayOneShot(windowOpenSound);
        yield return new WaitForSeconds(2f);
        killShield.SetActive(false);
    }

    [ContextMenu("Spawn Blood Decals")]
    public void SpawnBloodDecals()
    {
        StartCoroutine(SpawnRandomBloodDecals());
        StartCoroutine(SpawnBloodParticles());
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
