using UnityEngine;

public class HandOffPoint : Interactable
{
    public override void InteractWithItem(PlayerInteractionController playerInteractionController, PickableObject item)
    {
        base.InteractWithItem(playerInteractionController, item);
        if (item.ItemData.name == "Folder")
        {
            if (item.GetComponent<FolderController>().IsStamped)
            {
                MoveFolderToHandOffPoint();
            }
        }
    }
    
    void MoveFolderToHandOffPoint()
    {
        
    }
}
