using UnityEngine;

public class StackOfFolders : Interactable
{
    [SerializeField] private PickableItemData folder;
    private bool folderGrabbedAlready = false;
    [SerializeField] private string[] alreadyHaveFolderTutorialBarks;
    
    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        if (folderGrabbedAlready)
        {
            TutorialManager.Instance.ShowTutorialText(alreadyHaveFolderTutorialBarks[UnityEngine.Random.Range(0, alreadyHaveFolderTutorialBarks.Length)]);
            return;
        }

        folderGrabbedAlready = true;
        player.pickupController.SpawnAndPickUp(folder, transform);
    }
}
