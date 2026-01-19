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
    }
    IEnumerator SpawnRandomBloodDecals()
    {
        yield return new WaitForSeconds(.5f); 
        //Spawn Decals within box collider
        
        for (int i = 0; i < 4; i++)
        {
            Vector3 randomPos = new Vector3(
                UnityEngine.Random.Range(boxCollider.bounds.min.x, boxCollider.bounds.max.x),
                UnityEngine.Random.Range(boxCollider.bounds.min.y, boxCollider.bounds.max.y),
                UnityEngine.Random.Range(boxCollider.bounds.min.z, boxCollider.bounds.max.z)
            );

            //_bloodDecalManager.AddDecal(bloodDecalAsset, Color.red, randomPos, boxCollider.gameObject.transform.forward, Vector3.one);
            yield return new WaitForSeconds(UnityEngine.Random.Range(0.2f, 0.5f));
        }
    }
}
