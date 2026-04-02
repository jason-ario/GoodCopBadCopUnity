using UnityEngine;

public class PCFolderTab : MonoBehaviour
{
    [SerializeField] private Animator animator;
    
    public void SetFolderTabSelected(bool selected)
    {
        animator.SetBool("Selected", selected);
    }
}
