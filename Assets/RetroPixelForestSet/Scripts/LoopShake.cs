using UnityEngine;

namespace RetroPixelForestSet {
    /// <summary>
    /// Simple script for rotating object around its initial position.
    /// </summary>
    public class LoopShake : MonoBehaviour
    {
        public float shakeAmount = 0.01f;
        public float shakeSpeed = 1f;

        private Vector3 startPos;

        void Start()
        {
            startPos = transform.localPosition;
        }

        void Update()
        {
            var offsetX = Mathf.Sin(Time.time * shakeSpeed) * shakeAmount;
            var offsetZ = Mathf.Cos(Time.time * shakeSpeed) * shakeAmount;
            transform.localPosition = startPos + new Vector3(offsetX, 0, offsetZ);
        }
    }
}