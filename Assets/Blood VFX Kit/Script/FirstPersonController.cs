using UnityEngine;
using Biostart.Impact;
using Biostart.Blood;

namespace Biostart.Character
{
    public class FirstPersonController : MonoBehaviour
    {
        public float speed = 5f;
        public float mouseSensitivity = 2f;
        public Camera playerCamera;
        public float maxShootDistance = 100f;
        public GameObject crosshair;
        public Transform weaponTransform;

        private float xRotation = 0f;
        private float fireRate = 0.15f;
        private float nextFireTime = 0f;


        public Vector3 recoilKickback = new Vector3(0.02f, -0.02f, -0.1f);
        public float recoilRecoverySpeed = 5f;
        private Vector3 originalWeaponPosition;

        void Start()
        {
            originalWeaponPosition = weaponTransform.localPosition;
        }

        void Update()
        {
            HandleMovement();
            HandleLook();
            HandleShooting();
            RecoverRecoil();
        }

        void HandleMovement()
        {
            float moveX = Input.GetAxis("Horizontal");
            float moveZ = Input.GetAxis("Vertical");

            Vector3 move = transform.right * moveX + transform.forward * moveZ;
            transform.position += move * speed * Time.deltaTime;
        }

        void HandleLook()
        {
            float mouseX = Input.GetAxis("Mouse X") * mouseSensitivity;
            float mouseY = Input.GetAxis("Mouse Y") * mouseSensitivity;

            xRotation -= mouseY;
            xRotation = Mathf.Clamp(xRotation, -90f, 90f);

            playerCamera.transform.localRotation = Quaternion.Euler(xRotation, 0f, 0f);
            transform.Rotate(Vector3.up * mouseX);
        }

        void HandleShooting()
        {
            if (Input.GetMouseButton(0))
            {

                Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);
                RaycastHit hit;

                if (Physics.Raycast(ray, out hit))
                {


                    if (Time.time >= nextFireTime)
                    {
                        Shoot();
                        ApplyRecoil();
                        nextFireTime = Time.time + fireRate;
                    }
                }
            }
        }

        void Shoot()
        {
            RaycastHit hit;
            Vector3 rayOrigin = playerCamera.transform.position;
            Vector3 rayDirection = playerCamera.transform.forward;

            // Add you Code
            if (Physics.Raycast(rayOrigin, rayDirection, out hit, maxShootDistance))
            {

                // Impact effect
                ImpactEffect impact = hit.collider.GetComponent<ImpactEffect>();
                if (impact != null)
                {
                    // Spawn Blood Effect
                    impact.SpawnBloodEffect(hit.point, hit.normal);
                }

                // Add Blood - bullet 
                BloodProjector bloodProjector = hit.collider.GetComponent<BloodProjector>();
                if (bloodProjector != null)
                {
                    bloodProjector.AttachBloodProjector(hit.point, hit.normal, hit.collider);
                }

            }
        }




        void ApplyRecoil()
        {
            weaponTransform.localPosition += recoilKickback;
        }

        void RecoverRecoil()
        {
            weaponTransform.localPosition = Vector3.Lerp(
                weaponTransform.localPosition,
                originalWeaponPosition,
                Time.deltaTime * recoilRecoverySpeed
            );
        }
    }

}