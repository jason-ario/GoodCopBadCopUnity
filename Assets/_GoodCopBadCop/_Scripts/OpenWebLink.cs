using UnityEngine;

public class OpenWebLink : MonoBehaviour
{
    [SerializeField] private string url; 

    /// <summary>URL opened by <see cref="OpenWebPage"/>. Can be swapped at runtime (e.g. demo builds).</summary>
    public string Url
    {
        get => url;
        set => url = value;
    }

    public void OpenWebPage() 
    {
        Application.OpenURL(url); 
    }
}
