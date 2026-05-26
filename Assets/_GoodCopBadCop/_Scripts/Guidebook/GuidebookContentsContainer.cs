using UnityEngine;

/// <summary>
/// Singleton on the Guide Book Contents Container scene object.
/// Exposes Open/Close to toggle the contents child, which houses all
/// guidebook render cameras. The root object stays permanently active
/// so the singleton is always reachable at runtime.
/// </summary>
public class GuidebookContentsContainer : MonoBehaviour
{
    public static GuidebookContentsContainer Instance { get; private set; }

    [SerializeField] private GameObject _contents;

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

    /// <summary>Activates the contents child and all guidebook render cameras.</summary>
    public void Open() => _contents?.SetActive(true);

    /// <summary>Deactivates the contents child and all guidebook render cameras.</summary>
    public void Close() => _contents?.SetActive(false);
}
