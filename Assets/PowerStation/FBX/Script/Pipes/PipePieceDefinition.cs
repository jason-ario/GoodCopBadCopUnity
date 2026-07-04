using UnityEngine;

namespace PipeSystem
{
    [CreateAssetMenu(fileName = "NewPipePiece", menuName = "Pipe System/Piece Definition")]
    public class PipePieceDefinition : ScriptableObject
    {
        public string displayName;
        public GameObject prefab;
        public PipePieceType pieceType;
        public PipeDiameter diameterCategory;
        public PipeSocket[] sockets;

        [ContextMenu("Refresh Sockets From Prefab")]
        public void RefreshSockets()
        {
            if (prefab != null)
            {
                sockets = prefab.GetComponentsInChildren<PipeSocket>(true);
            }
        }
    }
}