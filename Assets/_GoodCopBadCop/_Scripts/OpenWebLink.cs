using UnityEngine;

public class OpenWebLink : MonoBehaviour
{
    [SerializeField] private string url; 
    public void OpenWebPage() 
    {
        Application.OpenURL(url); 
    }
}
