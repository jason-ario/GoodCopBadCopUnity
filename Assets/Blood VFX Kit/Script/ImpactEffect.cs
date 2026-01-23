using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Biostart.Impact
{
    public class ImpactEffect : MonoBehaviour
    {
        public List<GameObject> bloodEffectPrefabs; // List of blood effect prefabs
        public float bloodEffectPrefabsDestroy = 5f; // Destroy time
        public List<BloodEffectData> otherBloodEffectsData; // List of data for other blood effects
        public bool destroy = false; // Flag to destroy the object
        public bool disabled = false; // Flag to disable objects
        public List<GameObject> disabledObjects;  // List of objects to disable

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.collider != null)
            {
                ContactPoint contact = collision.contacts[0];
                Vector3 hitPosition = contact.point;
                Vector3 hitNormal = contact.normal;
                SpawnBloodEffect(hitPosition, hitNormal);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != null)
            {
                Vector3 hitPosition = other.ClosestPointOnBounds(transform.position);
                Vector3 hitNormal = other.transform.forward;
                SpawnBloodEffect(hitPosition, hitNormal);
            }
        }

        public void SpawnBloodEffect(Vector3 position, Vector3 normal)
        {
            Quaternion rotation = Quaternion.LookRotation(normal);

            // Instantiate the main blood effect from the list of prefabs
            foreach (var bloodEffectPrefab in bloodEffectPrefabs)
            {
                if (bloodEffectPrefab != null)
                {
                    GameObject effect = Instantiate(bloodEffectPrefab, position, rotation);
                    Destroy(effect, bloodEffectPrefabsDestroy); // Destroy after 5 seconds
                }
            }

            // Instantiate other blood effects based on their position and rotation
            foreach (var effectData in otherBloodEffectsData)
            {
                if (effectData.effect != null && effectData.positionObject != null)
                {
                    // Instantiate the effect inside positionObject
                    GameObject effect = Instantiate(effectData.effect, effectData.positionObject.transform);

                    // Inherit the full rotation from positionObject
                    effect.transform.rotation = effectData.positionObject.transform.rotation;

                    Destroy(effect, effectData.destroyTime);
                }
                else
                {
                    Debug.LogWarning("Effect or position object is not assigned in the list!");
                }
            }

            // Disable objects from the list if the disabled flag is active
            if (disabled)
            {
                foreach (GameObject obj in disabledObjects)
                {
                    if (obj != null)
                    {
                        obj.SetActive(false);
                    }
                    else
                    {
                        Debug.LogWarning("One of the disabled objects is null!");
                    }
                }
            }

            // Destroy the current object if the destroy flag is active
            if (destroy)
            {
                Destroy(gameObject);
            }
        }
    }

    [System.Serializable]
    public struct BloodEffectData
    {
        public GameObject effect;             // Blood effect
        public GameObject positionObject;     // Object from which the position is taken for spawning the effect
        public float destroyTime;             // Effect destruction time
    }
}