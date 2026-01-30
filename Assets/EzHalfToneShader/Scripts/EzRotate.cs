using UnityEngine;

namespace HalfToneDemo
{
    public class EzRotate : MonoBehaviour
    {
        [SerializeField] Transform m_target;
        [SerializeField] Vector3 m_rotSpeed = Vector3.one*90f;

        // Start is called once before the first execution of Update after the MonoBehaviour is created
        void Start()
        {
            if(!m_target)
            {
                m_target = transform;
            }
        }

        // Update is called once per frame
        void Update()
        {
            // Move the object up and down
            m_target.Rotate(m_rotSpeed * Time.deltaTime);
        }
    }
}
