using System;
using UnityEngine;

public class SettingsMenu : MonoBehaviour
{
    [SerializeField] private GameObject gameplaySettings;
    [SerializeField] private GameObject graphicsSettings;
    [SerializeField] private GameObject audioSettings;
    [SerializeField] private GameObject controlsSettings;
    [SerializeField] SelectableTab[] selectableTabs;

    private void OnEnable()
    {
        OpenGameplaySettings();
    }

    public void OpenGameplaySettings()
    {
        foreach (var selectableTab in selectableTabs)
        {
            selectableTab.SetSelected(false);
        }
        
        selectableTabs[0].SetSelected(true);
        gameplaySettings.SetActive(true);
        graphicsSettings.SetActive(false);
        audioSettings.SetActive(false);
        controlsSettings.SetActive(false);
    }
    
    public void OpenGraphicsSettings()
    {
        gameplaySettings.SetActive(false);
        graphicsSettings.SetActive(true);
        audioSettings.SetActive(false);
        controlsSettings.SetActive(false);
    }
    
    public void OpenAudioSettings()
    {
        gameplaySettings.SetActive(false);
        graphicsSettings.SetActive(false);
        audioSettings.SetActive(true);
        controlsSettings.SetActive(false);
    }
    
    public void OpenControlSettings()
    {
        gameplaySettings.SetActive(false);
        graphicsSettings.SetActive(false);
        audioSettings.SetActive(false);
        controlsSettings.SetActive(true);
    }
}
