using System.IO;
using UnityEngine;

public class SaveDataManager : MonoBehaviour
{
    public static SaveDataManager Instance { get; private set; }

    private SaveData _saveData;
    private string _savePath;

    // Public accessors
    public bool HasSeenIntroTutorial
    {
        get => _saveData.HasSeenTutorial;
        set
        {
            _saveData.HasSeenTutorial = value;
            Save();
        }
    }

    /// <summary>Returns true if a save file exists on disk with meaningful progress.</summary>
    public bool HasSaveFile => _saveData.HasSeenTutorial;

    private void Awake()
    {
        // Singleton setup
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        _savePath = Path.Combine(Application.persistentDataPath, "savedata.json");
        Load();
    }

    private void Save()
    {
        string json = JsonUtility.ToJson(_saveData, prettyPrint: true);
        File.WriteAllText(_savePath, json);
        Debug.Log($"[SaveDataManager] Game saved to: {_savePath}");
    }

    private void Load()
    {
        if (File.Exists(_savePath))
        {
            string json = File.ReadAllText(_savePath);
            _saveData = JsonUtility.FromJson<SaveData>(json);
            Debug.Log("[SaveDataManager] Save file loaded.");
        }
        else
        {
            _saveData = new SaveData();
            Debug.Log("[SaveDataManager] No save file found, created new SaveData.");
        }
    }

    [ContextMenu("Delete Save")]
    public void DeleteSave()
    {
        if (File.Exists(_savePath))
        {
            File.Delete(_savePath);
            Debug.Log("[SaveDataManager] Save file deleted.");
        }

        _saveData = new SaveData();
    }
}

[System.Serializable]
public class SaveData
{
    public bool HasSeenTutorial = false;
}
