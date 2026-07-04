using System.Collections.Generic;
using UnityEngine;

namespace PipeSystem
{
    public class PipeNetwork : MonoBehaviour
    {
        [Header("Network Validation")]
        [SerializeField] private List<PipeSocket> allSockets = new List<PipeSocket>();

        [ContextMenu("Scan & Validate Scene Network")]
        public void ValidateNetwork()
        {
            allSockets.Clear();
            // Updated for Unity 6: Use FindObjectsInactive.Exclude instead of FindObjectsSortMode
            allSockets.AddRange(FindObjectsByType<PipeSocket>(FindObjectsInactive.Exclude));

            int openSockets = 0;
            int diameterMismatches = 0;

            foreach (var socket in allSockets)
            {
                if (!socket.IsOccupied)
                {
                    openSockets++;
                    Debug.LogWarning($"[PipeNetwork] Open socket dead-end detected at {socket.transform.position}", socket);
                }
                else if (socket.category != socket.connectedTo.category)
                {
                    diameterMismatches++;
                    Debug.LogError($"[PipeNetwork] Diameter mismatch! {socket.category} connected to {socket.connectedTo.category} at {socket.transform.position}", socket);
                }
            }

            Debug.Log($"[PipeNetwork] Scan complete. Total Sockets: {allSockets.Count} | Open Dead-ends: {openSockets} | Mismatches: {diameterMismatches}");
        }
    }
}