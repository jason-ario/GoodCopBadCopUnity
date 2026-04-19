using UnityEngine;
using UnityEngine.WSA;

public class HandOffPoint : PlacementBoard
{
    public override void OnPlaced(PickableObject pickableObject)
    {
        base.OnPlaced(pickableObject);
        FolderController folderController = pickableObject.GetComponent<FolderController>();
        Debug.Log("OnPlaced");
        
        if (folderController == null) return;
        
        if (folderController.IsStamped)
        {
            SuspectController.Instance.DeliverVerdict(folderController);
        }
    }
}
