using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Biostart.Blood
{
    public class BloodProjector : MonoBehaviour
    {
        public GameObject bloodProjectorPrefab;
        public float zOffset = 0.1f;
        public float destroyTime = 25f; // Destroy time

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.collider != null)
            {
                ContactPoint contact = collision.contacts[0];
                Vector3 hitPosition = contact.point;
                Vector3 hitNormal = contact.normal;
                AttachBloodProjector(hitPosition, hitNormal, collision.collider);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other != null)
            {
                Vector3 hitPosition = other.ClosestPointOnBounds(transform.position);
                Vector3 hitNormal = other.transform.forward;
                AttachBloodProjector(hitPosition, hitNormal, other);
            }
        }

        public void AttachBloodProjector(Vector3 position, Vector3 normal, Collider hitObject)
        {
            if (bloodProjectorPrefab != null)
            {
                Vector3 correctedPosition = position + normal * zOffset;
                GameObject projector = Instantiate(bloodProjectorPrefab, correctedPosition, Quaternion.LookRotation(normal));

                // Попытка найти SkinnedMeshRenderer или кость в объекте
                Transform parentTransform = FindSkinnedMeshOrBone(hitObject.transform);
                if (parentTransform != null)
                {
                    projector.transform.SetParent(parentTransform);
                }
                else
                {
                    projector.transform.SetParent(hitObject.transform);
                }


                // Уничтожаем проектор через указанное время
                Destroy(projector, destroyTime);
            }
            else
            {
                Debug.LogError("bloodProjectorPrefab is not assigned!");
            }
        }


        private Transform FindSkinnedMeshOrBone(Transform objTransform)
        {
            SkinnedMeshRenderer skinnedMesh = objTransform.GetComponentInChildren<SkinnedMeshRenderer>();
            if (skinnedMesh != null)
            {
                return skinnedMesh.rootBone != null ? skinnedMesh.rootBone : skinnedMesh.transform;
            }
            return objTransform;
        }
    }
}