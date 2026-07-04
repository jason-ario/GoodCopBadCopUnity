using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public interface ISettingsMenuView
{
    void ShowTab(ESettingsMenuTab tab);
}

public class SettingsMenuView : MonoBehaviour, ISettingsMenuView
{
    private static readonly ESettingsMenuTab[] TabOrder =
    {
        ESettingsMenuTab.Gameplay,
        ESettingsMenuTab.Graphics,
        ESettingsMenuTab.Audio,
        ESettingsMenuTab.Controls
    };

    [SerializeField] private GameObject gameplaySettings;
    [SerializeField] private GameObject graphicsSettings;
    [SerializeField] private GameObject audioSettings;
    [SerializeField] private GameObject controlsSettings;
    [SerializeField] private SelectableTab[] selectableTabs;

    private readonly List<ESettingsMenuTab> availableTabs = new();

    private SettingsMenuModel model;
    private ISettingsMenuService service;
    private SettingsMenuPresenter presenter;

    private void Awake()
    {
        DisableDecorativeRaycastTargets();
        BindTabs();

        model = new SettingsMenuModel();
        service = new SettingsMenuService(model);
        presenter = new SettingsMenuPresenter(model, this);
    }

    private void OnEnable()
    {
        OpenDefaultSettings();
    }

    private void OnDestroy()
    {
        UnbindTabs();
        presenter?.Dispose();
        model?.Dispose();
    }

    public void OpenGameplaySettings()
    {
        service?.SelectTab(ESettingsMenuTab.Gameplay);
    }

    public void OpenGraphicsSettings()
    {
        service?.SelectTab(ESettingsMenuTab.Graphics);
    }

    public void OpenAudioSettings()
    {
        service?.SelectTab(ESettingsMenuTab.Audio);
    }

    public void OpenControlSettings()
    {
        service?.SelectTab(ESettingsMenuTab.Controls);
    }

    public void ShowTab(ESettingsMenuTab tab)
    {
        GameObject targetPanel = GetPanel(tab);

        SetPanelActive(gameplaySettings, targetPanel == gameplaySettings);
        SetPanelActive(graphicsSettings, targetPanel == graphicsSettings);
        SetPanelActive(audioSettings, targetPanel == audioSettings);
        SetPanelActive(controlsSettings, targetPanel == controlsSettings);

        for (int i = 0; i < selectableTabs.Length; i++)
        {
            if (selectableTabs[i] == null)
            {
                continue;
            }

            selectableTabs[i].SetSelected(GetTabAtIndex(i) == tab);
        }

        RebuildPanelLayout(targetPanel);
    }

    private void OpenDefaultSettings()
    {
        availableTabs.Clear();

        foreach (ESettingsMenuTab tab in TabOrder)
        {
            if (HasVisibleSettings(GetPanel(tab)))
            {
                availableTabs.Add(tab);
            }
        }

        service?.SelectDefaultTab(availableTabs);
    }

    private void BindTabs()
    {
        foreach (SelectableTab selectableTab in selectableTabs)
        {
            if (selectableTab == null)
            {
                continue;
            }

            selectableTab.Selected += SelectTabForButton;
        }
    }

    private void UnbindTabs()
    {
        foreach (SelectableTab selectableTab in selectableTabs)
        {
            if (selectableTab == null)
            {
                continue;
            }

            selectableTab.Selected -= SelectTabForButton;
        }
    }

    private void SelectTabForButton(SelectableTab selectableTab)
    {
        for (int i = 0; i < selectableTabs.Length; i++)
        {
            if (selectableTabs[i] != selectableTab)
            {
                continue;
            }

            service?.SelectTab(GetTabAtIndex(i));
            return;
        }
    }

    private static ESettingsMenuTab GetTabAtIndex(int index)
    {
        return index >= 0 && index < TabOrder.Length
            ? TabOrder[index]
            : ESettingsMenuTab.Graphics;
    }

    private GameObject GetPanel(ESettingsMenuTab tab)
    {
        switch (tab)
        {
            case ESettingsMenuTab.Gameplay:
                return gameplaySettings;
            case ESettingsMenuTab.Graphics:
                return graphicsSettings;
            case ESettingsMenuTab.Audio:
                return audioSettings;
            case ESettingsMenuTab.Controls:
                return controlsSettings;
            default:
                return graphicsSettings;
        }
    }

    private static void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null)
        {
            panel.SetActive(active);
        }
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
