using Unity.Netcode;
using UnityEngine;

public class PlayerSpawnPos : MonoBehaviour
{
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(transform.position, 0.3f);
    }
}