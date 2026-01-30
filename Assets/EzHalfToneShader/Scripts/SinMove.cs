using UnityEngine;

namespace HalfToneDemo
{
    public class SinMove : MonoBehaviour
    {
        [SerializeField] float m_waveHeight = 0.2f;
        [SerializeField] float m_waveSpeed = 1f;
        Vector3 m_startPosition;
        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            m_startPosition = transform.position;
        }

        // Update is called once per frame
        void Update()
        {
            // Move the object up and down
            transform.position = m_startPosition + new Vector3(0f, (Mathf.Sin(Time.time* m_waveSpeed) +1f) * m_waveHeight, 0f);

        }
    }
}
