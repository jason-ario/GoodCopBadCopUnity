using UnityEngine;
using UnityEngine.UI;

public class SettingsMenu : MonoBehaviour
{
    [SerializeField] private GameObject gameplaySettings;
    [SerializeField] private GameObject graphicsSettings;
    [SerializeField] private GameObject audioSettings;
    [SerializeField] private GameObject controlsSettings;
    [SerializeField] SelectableTab[] selectableTabs;

    private void Awake()
    {
        DisableDecorativeRaycastTargets();
    }

    private void OnEnable()
    {
        OpenDefaultSettings();
    }

    public void OpenGameplaySettings()
    {
        OpenSettingsPanel(gameplaySettings, 0);
    }
    
    public void OpenGraphicsSettings()
    {
        OpenSettingsPanel(graphicsSettings, 1);
    }
    
    public void OpenAudioSettings()
    {
        OpenSettingsPanel(audioSettings, 2);
    }
    
    public void OpenControlSettings()
    {
        OpenSettingsPanel(controlsSettings, 3);
    }

    public bool OpenSettingsForTab(SelectableTab selectableTab)
    {
        for (int i = 0; i < selectableTabs.Length; i++)
        {
            if (selectableTabs[i] != selectableTab)
            {
                continue;
            }

            OpenSettingsByIndex(i);
            return true;
        }

        return false;
    }

    private void OpenDefaultSettings()
    {
        if (HasVisibleSettings(gameplaySettings))
        {
            OpenSettingsPanel(gameplaySettings, 0);
            return;
        }

        if (HasVisibleSettings(graphicsSettings))
        {
            OpenSettingsPanel(graphicsSettings, 1);
            return;
        }

        if (HasVisibleSettings(audioSettings))
        {
            OpenSettingsPanel(audioSettings, 2);
            return;
        }

        OpenSettingsPanel(controlsSettings, 3);
    }

    private void OpenSettingsByIndex(int index)
    {
        switch (index)
        {
            case 0:
                OpenGameplaySettings();
                break;
            case 1:
                OpenGraphicsSettings();
                break;
            case 2:
                OpenAudioSettings();
                break;
            case 3:
                OpenControlSettings();
                break;
        }
    }

    private void OpenSettingsPanel(GameObject targetPanel, int selectedTabIndex)
    {
        gameplaySettings.SetActive(targetPanel == gameplaySettings);
        graphicsSettings.SetActive(targetPanel == graphicsSettings);
        audioSettings.SetActive(targetPanel == audioSettings);
        controlsSettings.SetActive(targetPanel == controlsSettings);

        for (int i = 0; i < selectableTabs.Length; i++)
        {
            selectableTabs[i].SetSelected(i == selectedTabIndex);
        }

        RebuildPanelLayout(targetPanel);
    }

    private static bool HasVisibleSettings(GameObject settingsPanel)
    {
        if (settingsPanel == null)
        {
            return false;
        }

        ScrollRect scrollRect = settingsPanel.GetComponent<ScrollRect>();
        Transform content = scrollRect != null && scrollRect.content != null
            ? scrollRect.content
            : settingsPanel.transform;

        for (int i = 0; i < content.childCount; i++)
        {
            if (content.GetChild(i).gameObject.activeSelf)
            {
                return true;
            }
        }

        return false;
    }

    private static void RebuildPanelLayout(GameObject settingsPanel)
    {
        if (settingsPanel == null)
        {
            return;
        }

        Canvas.ForceUpdateCanvases();

        RectTransform[] rectTransforms = settingsPanel.GetComponentsInChildren<RectTransform>(true);
        foreach (RectTransform rectTransform in rectTransforms)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
        }

        Canvas.ForceUpdateCanvases();
    }

    private void DisableDecorativeRaycastTargets()
    {
        DisableImageRaycastTarget(gameObject);
        DisableImageRaycastTarget(transform.Find("Settings Side Bar"));
        DisableImageRaycastTarget(transform.Find("Settings Main View"));

        for (int i = 0; i < transform.childCount; i++)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("Line"))
            {
                DisableImageRaycastTarget(child);
            }
        }
    }

    private static void DisableImageRaycastTarget(Transform target)
    {
        if (target == null)
        {
            return;
        }

        DisableImageRaycastTarget(target.gameObject);
    }

    private static void DisableImageRaycastTarget(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        Image image = target.GetComponent<Image>();
        if (image != null)
        {
            image.raycastTarget = false;
        }
    }
}
