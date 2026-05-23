using UnityEngine;

/// <summary>
/// Singleton that lives on the Guide Book Contents Container scene object.
/// Allows runtime systems such as GuidebookController to locate and toggle
/// the container without a cross-prefab serialized reference.
/// </summary>
public class GuidebookContentsContainer : MonoBehaviour
{
    public static GuidebookContentsContainer Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("[GuidebookContentsContainer] Duplicate instance detected. Destroying this one.", this);
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
}
