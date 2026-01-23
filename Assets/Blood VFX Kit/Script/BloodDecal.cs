using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Biostart.Blood
{
    public class BloodDecal : MonoBehaviour
    {
        public List<GameObject> decalPrefabs; // List of decal prefabs
        public bool randomRotation = true; // Rotation on spawn
        public float minScale = 1f; // Minimum decal size
        public float maxScale = 1.5f; // Maximum decal size
        public float destroyDelay = 20f; // Time before decal destruction
        public string[] targetTags; // Array of tags where the decal will appear
        public float spawnChance = 0.7f; // Chance of spawning a decal (0.0 - 1.0)

        private ParticleSystem system;
        private List<ParticleCollisionEvent> collisionEvents = new List<ParticleCollisionEvent>(); // Particle collision events

        void Awake()
        {
            system = GetComponent<ParticleSystem>();
        }

        private void OnParticleCollision(GameObject other)
        {
            return;
            // Check if the object has at least one of the target tags
            foreach (string tag in targetTags)
            {
                if (other.CompareTag(tag))
                {
                    int numCollisionEvents = system.GetCollisionEvents(other, collisionEvents); // Get collision events

                    for (int i = 0; i < numCollisionEvents; i++)
                    {
                        // Check the chance of decal spawning
                        if (Random.value > spawnChance)
                            continue;

                        // Collision position and normal
                        Vector3 pos = collisionEvents[i].intersection;
                        Vector3 normal = collisionEvents[i].normal;

                        // Set rotation for the decal
                        Quaternion rotation = randomRotation
                            ? Quaternion.AngleAxis(Random.Range(0f, 360f), normal) * Quaternion.LookRotation(-normal)
                            : Quaternion.LookRotation(-normal, Vector3.up);

                        // Select a random prefab from the list
                        GameObject decalPrefab = decalPrefabs[Random.Range(0, decalPrefabs.Count)];

                        // Spawn the decal at the collision position
                        GameObject decal = Instantiate(decalPrefab, pos + normal * 0.0002f, rotation);
                        decal.transform.localScale *= Random.Range(minScale, maxScale); // Scale the decal
                        Destroy(decal, destroyDelay); // Destroy the decal after a delay
                    }
                    break; // Stop checking if at least one tag matches
                }
            }
        }
    }
}