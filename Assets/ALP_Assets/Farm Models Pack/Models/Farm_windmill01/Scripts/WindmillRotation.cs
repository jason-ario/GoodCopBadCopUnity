using UnityEngine;

public class WindmillRotation : MonoBehaviour
{
    [SerializeField]
    private Transform m_Blades;
     [SerializeField]
    private Vector3 m_BladesSpeed;

    [Space]

    [SerializeField]
    private Transform m_Support;

    [SerializeField]
    private Vector3 m_SupportRotationAmount;

    [SerializeField]
    private float m_SupportRotationSpeed = 1f;


    private void Update()
    {
        if(m_Blades != null)
        {
            m_Blades.localRotation *= Quaternion.Euler(m_BladesSpeed * Time.deltaTime);
            m_Support.localRotation = Quaternion.Euler(m_SupportRotationAmount * Mathf.Cos(Time.time * m_SupportRotationSpeed));
        }
    }
}