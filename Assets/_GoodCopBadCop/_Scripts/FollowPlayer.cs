using Unity.Netcode;
using UnityEngine;

public class FollowPlayer : MonoBehaviour
{
    [SerializeField] private Vector3 offset;

    // Update is called once per frame
    void Update()
    {
        if(PlayerInstance.Instance != null)
        {
            transform.position = PlayerInstance.Instance.transform.position + offset;
        }
    }
}
