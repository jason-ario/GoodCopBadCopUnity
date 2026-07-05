using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine.EventSystems;
using UnityEngine;
using UnityEngine.UI;
using VContainer;

namespace GoodCopBadCop.UI.SettingsMenu
{
    public interface ISettingsMenuView
    {
        event Action<int> DisplayModeChanged;
        event Action Closed;
        void ShowTab(ESettingsMenuTab tab);
        void SetDisplayModeValue(int value);
    }

    public class SettingsMenuView : MonoBehaviour, ISettingsMenuView
    {
        private const float SettingsRowHeight = 100f;
        private const float DropdownWidth = 320f;
        private const float DropdownHeight = 58f;
        private const float DropdownItemHeight = 54f;
        private const float DropdownMinPopupVisibleItems = 3f;
        private const float DropdownMaxPopupHeight = 324f;

        private enum ESettingsMenuControlType
        {
            Slider,
            Dropdown
        }

        private enum ESettingsMenuControlOption
        {
            None,
            DisplayMode
        }

        private sealed class SettingsMenuControlDefinition
        {
            public readonly ESettingsMenuControlOption Option;
            public readonly string Label;
            public readonly ESettingsMenuControlType ControlType;
            public readonly string[] Options;
            public readonly int DefaultOptionIndex;
            public readonly float SliderMinValue;
            public readonly float SliderMaxValue;
            public readonly float SliderValue;
            public readonly bool Interactable;

            private SettingsMenuControlDefinition(
                ESettingsMenuControlOption option,
                string label,
                ESettingsMenuControlType controlType,
                string[] options,
                int defaultOptionIndex,
                float sliderMinValue,
                float sliderMaxValue,
                float sliderValue,
                bool interactable)
            {
                Option = option;
                Label = label;
                ControlType = controlType;
                Options = options;
                DefaultOptionIndex = defaultOptionIndex;
                SliderMinValue = sliderMinValue;
                SliderMaxValue = sliderMaxValue;
                SliderValue = sliderValue;
                Interactable = interactable;
            }

            public static SettingsMenuControlDefinition Slider(string label, float value = 100f)
            {
                return new SettingsMenuControlDefinition(
                    ESettingsMenuControlOption.None,
                    label,
                    ESettingsMenuControlType.Slider,
                    null,
                    0,
                    0f,
                    100f,
                    value,
                    true);
            }

            public static SettingsMenuControlDefinition Dropdown(
                string label,
                string[] options,
                int defaultOptionIndex = 0,
                bool interactable = true,
                ESettingsMenuControlOption option = ESettingsMenuControlOption.None)
            {
                return new SettingsMenuControlDefinition(
                    option,
                    label,
                    ESettingsMenuControlType.Dropdown,
                    options,
                    defaultOptionIndex,
                    0f,
                    0f,
                    0f,
                    interactable);
            }
        }

        private static readonly string[] OffOnOptions = { "Off", "On" };
        private static readonly string[] HoldToggleOptions = { "Hold", "Toggle" };
        private static readonly string[] DisplayModeOptions = { "Fullscreen", "Borderless", "Windowed" };

        private static readonly ESettingsMenuTab[] TabOrder =
        {
            ESettingsMenuTab.Gameplay,
            ESettingsMenuTab.Graphics,
            ESettingsMenuTab.Audio,
            ESettingsMenuTab.Controls
        };

        private static readonly SettingsMenuControlDefinition[] GameplayControlDefinitions =
        {
            SettingsMenuControlDefinition.Dropdown("Language", new[] { "English" }, interactable: false),
            SettingsMenuControlDefinition.Dropdown("Subtitles", OffOnOptions, defaultOptionIndex: 1, interactable: false),
            SettingsMenuControlDefinition.Dropdown("Camera Shake", OffOnOptions, defaultOptionIndex: 1),
            SettingsMenuControlDefinition.Dropdown("Head Bob", OffOnOptions, defaultOptionIndex: 1)
        };

        private static readonly SettingsMenuControlDefinition[] GraphicsControlDefinitions =
        {
            SettingsMenuControlDefinition.Dropdown(
                "Display Mode",
                DisplayModeOptions,
                option: ESettingsMenuControlOption.DisplayMode),
            SettingsMenuControlDefinition.Dropdown("Resolution", new[] { "1920 x 1080", "1600 x 900", "1280 x 720" }),
            SettingsMenuControlDefinition.Dropdown("VSync", OffOnOptions, defaultOptionIndex: 1),
            SettingsMenuControlDefinition.Dropdown("FPS Limit", new[] { "Unlimited", "30", "60", "120", "144" }),
            SettingsMenuControlDefinition.Dropdown("Quality Preset", new[] { "Low", "Medium", "High", "Ultra" }, defaultOptionIndex: 2),
            SettingsMenuControlDefinition.Slider("Brightness", 50f),
            SettingsMenuControlDefinition.Dropdown("Film Grain", OffOnOptions, defaultOptionIndex: 1),
            SettingsMenuControlDefinition.Dropdown("Chromatic Aberration", OffOnOptions, defaultOptionIndex: 1)
        };

        private static readonly SettingsMenuControlDefinition[] AudioControlDefinitions =
        {
            SettingsMenuControlDefinition.Slider("Master Volume"),
            SettingsMenuControlDefinition.Slider("Music Volume"),
            SettingsMenuControlDefinition.Slider("SFX Volume"),
            SettingsMenuControlDefinition.Slider("Voice Volume"),
            SettingsMenuControlDefinition.Slider("Ambient Volume")
        };

        private static readonly SettingsMenuControlDefinition[] ControlsControlDefinitions =
        {
            SettingsMenuControlDefinition.Slider("Mouse Sensitivity", 50f),
            SettingsMenuControlDefinition.Dropdown("Invert Y Axis", OffOnOptions),
            SettingsMenuControlDefinition.Dropdown("Crouch Mode", HoldToggleOptions),
            SettingsMenuControlDefinition.Dropdown("Sprint Mode", HoldToggleOptions),
            SettingsMenuControlDefinition.Dropdown("Move Forward", new[] { "W" }),
            SettingsMenuControlDefinition.Dropdown("Move Backward", new[] { "S" }),
            SettingsMenuControlDefinition.Dropdown("Move Left", new[] { "A" }),
            SettingsMenuControlDefinition.Dropdown("Move Right", new[] { "D" }),
            SettingsMenuControlDefinition.Dropdown("Jump", new[] { "Space" }),
            SettingsMenuControlDefinition.Dropdown("Interact", new[] { "E" }),
            SettingsMenuControlDefinition.Dropdown("Pause", new[] { "Esc" })
        };

        [SerializeField] private GameObject gameplaySettings;
        [SerializeField] private GameObject graphicsSettings;
        [SerializeField] private GameObject audioSettings;
        [SerializeField] private GameObject controlsSettings;
        [SerializeField] private SelectableTab[] selectableTabs;

        private readonly List<ESettingsMenuTab> availableTabs = new();

        private ISettingsMenuService service;
        private TMP_Dropdown displayModeDropdown;

        public event Action<int> DisplayModeChanged;
        public event Action Closed;

        [Inject]
        public void Construct(ISettingsMenuService service)
        {
            this.service = service;
        }

        private void Awake()
        {
            DisableDecorativeRaycastTargets();
            BuildSettingsContent();
            BindTabs();
        }

        private void OnEnable()
        {
            OpenDefaultSettings();
        }

        private void OnDisable()
        {
            Closed?.Invoke();
        }

        private void OnDestroy()
        {
            UnbindTabs();
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

        public void SetDisplayModeValue(int value)
        {
            if (displayModeDropdown == null)
            {
                return;
            }

            displayModeDropdown.SetValueWithoutNotify(Mathf.Clamp(value, 0, displayModeDropdown.options.Count - 1));
            displayModeDropdown.RefreshShownValue();
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

        private void BuildSettingsContent()
        {
            GameObject sliderTemplate = CreateTemplateCopy(FindSettingsRowTemplate<Slider>());
            GameObject dropdownTemplate = CreateTemplateCopy(FindSettingsRowTemplate<TMP_Dropdown>());

            if (sliderTemplate == null || dropdownTemplate == null)
            {
                Debug.LogWarning("[SettingsMenuView] Settings row templates are missing. Content will keep prefab defaults.", this);
                DestroyTemplate(sliderTemplate);
                DestroyTemplate(dropdownTemplate);
                return;
            }

            PopulateSettingsPanel(gameplaySettings, GameplayControlDefinitions, sliderTemplate, dropdownTemplate);
            PopulateSettingsPanel(graphicsSettings, GraphicsControlDefinitions, sliderTemplate, dropdownTemplate);
            PopulateSettingsPanel(audioSettings, AudioControlDefinitions, sliderTemplate, dropdownTemplate);
            PopulateSettingsPanel(controlsSettings, ControlsControlDefinitions, sliderTemplate, dropdownTemplate);

            DestroyTemplate(sliderTemplate);
            DestroyTemplate(dropdownTemplate);
        }

        private void PopulateSettingsPanel(
            GameObject settingsPanel,
            IReadOnlyList<SettingsMenuControlDefinition> definitions,
            GameObject sliderTemplate,
            GameObject dropdownTemplate)
        {
            Transform content = GetSettingsContent(settingsPanel);
            if (content == null)
            {
                return;
            }

            ClearSettingsContent(content);
            ConfigureScrollableContent(settingsPanel, content);

            foreach (SettingsMenuControlDefinition definition in definitions)
            {
                GameObject template = definition.ControlType == ESettingsMenuControlType.Slider
                    ? sliderTemplate
                    : dropdownTemplate;

                GameObject row = Instantiate(template, content);
                row.name = definition.Label;
                row.hideFlags = HideFlags.None;
                row.SetActive(true);
                row.transform.SetAsLastSibling();

                SetRowLabel(row, definition.Label);

                if (definition.ControlType == ESettingsMenuControlType.Slider)
                {
                    ConfigureSliderRow(row, definition);
                }
                else
                {
                    ConfigureDropdownRow(row, definition);
                }
            }

            UpdateContentHeight(settingsPanel, content);
        }

        private GameObject FindSettingsRowTemplate<TComponent>() where TComponent : Component
        {
            foreach (ESettingsMenuTab tab in TabOrder)
            {
                Transform content = GetSettingsContent(GetPanel(tab));
                if (content == null)
                {
                    continue;
                }

                TComponent component = content.GetComponentInChildren<TComponent>(true);
                if (component == null)
                {
                    continue;
                }

                Transform row = component.transform;
                while (row.parent != null && row.parent != content)
                {
                    row = row.parent;
                }

                if (row.parent == content)
                {
                    return row.gameObject;
                }
            }

            return null;
        }

        private GameObject CreateTemplateCopy(GameObject template)
        {
            if (template == null)
            {
                return null;
            }

            GameObject templateCopy = Instantiate(template, transform);
            templateCopy.name = $"{template.name} Template";
            templateCopy.hideFlags = HideFlags.HideAndDontSave;
            templateCopy.SetActive(false);
            return templateCopy;
        }

        private static void DestroyTemplate(GameObject template)
        {
            if (template != null)
            {
                Destroy(template);
            }
        }

        private static Transform GetSettingsContent(GameObject settingsPanel)
        {
            if (settingsPanel == null)
            {
                return null;
            }

            ScrollRect scrollRect = settingsPanel.GetComponent<ScrollRect>();
            return scrollRect != null && scrollRect.content != null
                ? scrollRect.content
                : settingsPanel.transform;
        }

        private static void ClearSettingsContent(Transform content)
        {
            for (int i = content.childCount - 1; i >= 0; i--)
            {
                GameObject child = content.GetChild(i).gameObject;
                child.SetActive(false);
                Destroy(child);
            }
        }

        private static void ConfigureScrollableContent(GameObject settingsPanel, Transform content)
        {
            ScrollRect scrollRect = settingsPanel.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 30f;
                scrollRect.content = content as RectTransform;
                scrollRect.verticalNormalizedPosition = 1f;
                StyleScrollRect(scrollRect);
            }

            RectTransform contentRect = content as RectTransform;
            if (contentRect != null)
            {
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0f, 1f);
                contentRect.anchoredPosition = Vector2.zero;
            }

            VerticalLayoutGroup layoutGroup = content.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                layoutGroup.childAlignment = TextAnchor.UpperLeft;
                layoutGroup.childControlWidth = false;
                layoutGroup.childControlHeight = false;
                layoutGroup.childForceExpandWidth = true;
                layoutGroup.childForceExpandHeight = false;
                layoutGroup.spacing = 0f;
            }
        }

        private static void UpdateContentHeight(GameObject settingsPanel, Transform content)
        {
            RectTransform contentRect = content as RectTransform;
            if (contentRect == null)
            {
                return;
            }

            float contentHeight = 0f;
            int activeRows = 0;

            for (int i = 0; i < content.childCount; i++)
            {
                GameObject child = content.GetChild(i).gameObject;
                if (!child.activeSelf)
                {
                    continue;
                }

                RectTransform childRect = child.transform as RectTransform;
                contentHeight += childRect != null
                    ? Mathf.Max(childRect.sizeDelta.y, childRect.rect.height, SettingsRowHeight)
                    : SettingsRowHeight;
                activeRows++;
            }

            VerticalLayoutGroup layoutGroup = content.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                contentHeight += layoutGroup.padding.top + layoutGroup.padding.bottom;
                contentHeight += Mathf.Max(0, activeRows - 1) * layoutGroup.spacing;
            }

            ScrollRect scrollRect = settingsPanel.GetComponent<ScrollRect>();
            RectTransform viewport = scrollRect != null ? scrollRect.viewport : null;
            float viewportHeight = viewport != null ? viewport.rect.height : 0f;

            contentRect.SetSizeWithCurrentAnchors(
                RectTransform.Axis.Vertical,
                Mathf.Max(contentHeight, viewportHeight));

            if (scrollRect != null)
            {
                scrollRect.verticalNormalizedPosition = 1f;
            }
        }

        private static void SetRowLabel(GameObject row, string label)
        {
            TMP_Text labelText = FindRowLabel(row);
            if (labelText != null)
            {
                labelText.text = label;
            }
        }

        private static TMP_Text FindRowLabel(GameObject row)
        {
            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (text.name == "Text")
                {
                    return text;
                }
            }

            foreach (TMP_Text text in texts)
            {
                if (text.GetComponentInParent<TMP_Dropdown>() == null && text.name != "Slider Text")
                {
                    return text;
                }
            }

            return texts.Length > 0 ? texts[0] : null;
        }

        private static void ConfigureSliderRow(GameObject row, SettingsMenuControlDefinition definition)
        {
            Slider slider = row.GetComponentInChildren<Slider>(true);
            if (slider == null)
            {
                return;
            }

            slider.minValue = definition.SliderMinValue;
            slider.maxValue = definition.SliderMaxValue;
            slider.wholeNumbers = true;
            slider.value = definition.SliderValue;
            slider.interactable = definition.Interactable;
            SetSliderValueText(row, definition.SliderValue);
        }

        private static void SetSliderValueText(GameObject row, float value)
        {
            TMP_Text[] texts = row.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                if (text.name == "Slider Text")
                {
                    text.text = value.ToString("0");
                    return;
                }
            }
        }

        private void ConfigureDropdownRow(GameObject row, SettingsMenuControlDefinition definition)
        {
            TMP_Dropdown dropdown = row.GetComponentInChildren<TMP_Dropdown>(true);
            if (dropdown == null)
            {
                return;
            }

            dropdown.ClearOptions();
            dropdown.AddOptions(new List<string>(definition.Options));

            if (definition.Option == ESettingsMenuControlOption.DisplayMode)
            {
                displayModeDropdown = dropdown;
            }

            dropdown.SetValueWithoutNotify(Mathf.Clamp(definition.DefaultOptionIndex, 0, definition.Options.Length - 1));
            dropdown.interactable = definition.Interactable;
            dropdown.RefreshShownValue();

            if (definition.Interactable && definition.Option == ESettingsMenuControlOption.DisplayMode)
            {
                dropdown.onValueChanged.AddListener(value => DisplayModeChanged?.Invoke(value));
            }

            ConfigureDropdownSize(dropdown, definition.Options.Length);
            StyleDropdown(row, dropdown, definition.Interactable);
        }

        private static float GetDropdownPopupHeight(int optionCount)
        {
            float contentHeight = Mathf.Max(optionCount, DropdownMinPopupVisibleItems) * DropdownItemHeight;
            return Mathf.Min(contentHeight, DropdownMaxPopupHeight);
        }

        private static void ApplyDropdownFont(TMP_Text fontSource, TMP_Dropdown dropdown)
        {
            ApplyFont(fontSource, dropdown.captionText);
            ApplyFont(fontSource, dropdown.itemText);
        }

        private static void ApplyFont(TMP_Text source, TMP_Text target)
        {
            if (source == null || target == null || source.font == null)
            {
                return;
            }

            target.font = source.font;
            target.fontSharedMaterial = source.fontSharedMaterial;
            target.fontStyle = source.fontStyle;
        }

        private static T GetOrAddComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            return component != null ? component : target.AddComponent<T>();
        }

        private static void StretchToFill(RectTransform rectTransform)
        {
            if (rectTransform == null)
            {
                return;
            }

            rectTransform.localScale = Vector3.one;
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;
        }

        private static void ConfigureDropdownSize(TMP_Dropdown dropdown, int optionCount)
        {
            RectTransform dropdownRect = dropdown.transform as RectTransform;
            if (dropdownRect != null)
            {
                dropdownRect.localScale = Vector3.one;
                dropdownRect.anchorMin = new Vector2(1f, 0.5f);
                dropdownRect.anchorMax = new Vector2(1f, 0.5f);
                dropdownRect.pivot = new Vector2(1f, 0.5f);
                dropdownRect.anchoredPosition = new Vector2(-34f, 0f);
                dropdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, DropdownWidth);
                dropdownRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, DropdownHeight);
            }

            RectTransform captionRect = dropdown.captionText != null
                ? dropdown.captionText.transform as RectTransform
                : null;
            if (captionRect != null)
            {
                captionRect.localScale = Vector3.one;
                captionRect.anchorMin = Vector2.zero;
                captionRect.anchorMax = Vector2.one;
                captionRect.offsetMin = new Vector2(18f, 0f);
                captionRect.offsetMax = new Vector2(-44f, 0f);
            }

            RectTransform arrowRect = dropdown.transform.Find("Arrow") as RectTransform;
            if (arrowRect != null)
            {
                arrowRect.localScale = Vector3.one;
                arrowRect.anchorMin = new Vector2(1f, 0.5f);
                arrowRect.anchorMax = new Vector2(1f, 0.5f);
                arrowRect.pivot = new Vector2(0.5f, 0.5f);
                arrowRect.anchoredPosition = new Vector2(-22f, 0f);
                arrowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 18f);
                arrowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 18f);
            }

            if (dropdown.template != null)
            {
                float popupHeight = GetDropdownPopupHeight(optionCount);
                dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, DropdownWidth);
                dropdown.template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, popupHeight);
            }
        }

        private static void StyleDropdown(GameObject row, TMP_Dropdown dropdown, bool interactable)
        {
            Color textColor = interactable ? Color.white : new Color(0.65f, 0.65f, 0.65f, 1f);
            Color panelColor = new(0f, 0f, 0f, interactable ? 0.72f : 0.45f);
            TMP_Text labelText = FindRowLabel(row);
            if (labelText != null)
            {
                labelText.color = textColor;
            }

            if (dropdown.captionText != null)
            {
                dropdown.captionText.color = textColor;
                dropdown.captionText.alignment = TextAlignmentOptions.MidlineLeft;
                dropdown.captionText.enableAutoSizing = true;
                dropdown.captionText.fontSizeMin = 18f;
                dropdown.captionText.fontSizeMax = 30f;
            }

            if (dropdown.itemText != null)
            {
                dropdown.itemText.color = Color.white;
                dropdown.itemText.alignment = TextAlignmentOptions.MidlineLeft;
                dropdown.itemText.enableAutoSizing = true;
                dropdown.itemText.fontSizeMin = 18f;
                dropdown.itemText.fontSizeMax = 28f;
            }

            ApplyDropdownFont(labelText, dropdown);

            Image dropdownImage = dropdown.GetComponent<Image>();
            if (dropdownImage != null)
            {
                dropdownImage.color = panelColor;
                dropdownImage.raycastTarget = true;
            }

            ColorBlock colors = dropdown.colors;
            colors.normalColor = panelColor;
            colors.highlightedColor = panelColor;
            colors.pressedColor = panelColor;
            colors.selectedColor = panelColor;
            colors.disabledColor = new Color(0f, 0f, 0f, 0.35f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0f;
            dropdown.colors = colors;

            Image arrowImage = dropdown.transform.Find("Arrow")?.GetComponent<Image>();
            if (arrowImage != null)
            {
                arrowImage.color = textColor;
                arrowImage.raycastTarget = false;
            }

            if (dropdown.template != null)
            {
                StyleDropdownTemplate(dropdown.template, dropdown.options.Count, labelText);
            }
        }

        private static void StyleDropdownTemplate(RectTransform template, int optionCount, TMP_Text fontSource)
        {
            float popupHeight = GetDropdownPopupHeight(optionCount);
            template.localScale = Vector3.one;
            template.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, DropdownWidth);
            template.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, popupHeight);

            Image templateImage = template.GetComponent<Image>();
            if (templateImage != null)
            {
                templateImage.color = new Color(0f, 0f, 0f, 0.94f);
                templateImage.raycastTarget = true;
            }

            Image viewportImage = template.Find("Viewport")?.GetComponent<Image>();
            if (viewportImage != null)
            {
                viewportImage.color = new Color(0f, 0f, 0f, 0.94f);
                viewportImage.raycastTarget = true;
            }

            RectTransform viewportRect = template.Find("Viewport") as RectTransform;
            if (viewportRect != null)
            {
                viewportRect.localScale = Vector3.one;
                viewportRect.anchorMin = Vector2.zero;
                viewportRect.anchorMax = Vector2.one;
                viewportRect.offsetMin = new Vector2(0f, 0f);
                viewportRect.offsetMax = new Vector2(-12f, 0f);
            }

            RectTransform contentRect = template.Find("Viewport/Content") as RectTransform;
            if (contentRect != null)
            {
                contentRect.localScale = Vector3.one;
                contentRect.anchorMin = new Vector2(0f, 1f);
                contentRect.anchorMax = new Vector2(1f, 1f);
                contentRect.pivot = new Vector2(0f, 1f);
                contentRect.SetSizeWithCurrentAnchors(
                    RectTransform.Axis.Vertical,
                    Mathf.Max(optionCount, 1) * DropdownItemHeight);

                VerticalLayoutGroup contentLayout = GetOrAddComponent<VerticalLayoutGroup>(contentRect.gameObject);
                contentLayout.childAlignment = TextAnchor.UpperLeft;
                contentLayout.childControlWidth = true;
                contentLayout.childControlHeight = false;
                contentLayout.childForceExpandWidth = true;
                contentLayout.childForceExpandHeight = false;
                contentLayout.spacing = 0f;

                ContentSizeFitter contentFitter = GetOrAddComponent<ContentSizeFitter>(contentRect.gameObject);
                contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }

            RectTransform itemRect = template.Find("Viewport/Content/Item") as RectTransform;
            if (itemRect != null)
            {
                itemRect.localScale = Vector3.one;
                itemRect.anchorMin = new Vector2(0f, 1f);
                itemRect.anchorMax = new Vector2(1f, 1f);
                itemRect.pivot = new Vector2(0f, 1f);
                itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, DropdownWidth - 12f);
                itemRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, DropdownItemHeight);

                LayoutElement itemLayout = GetOrAddComponent<LayoutElement>(itemRect.gameObject);
                itemLayout.minHeight = DropdownItemHeight;
                itemLayout.preferredHeight = DropdownItemHeight;
                itemLayout.flexibleHeight = 0f;
            }

            RectTransform itemBackgroundRect = template.Find("Viewport/Content/Item/Item Background") as RectTransform;
            if (itemBackgroundRect != null)
            {
                itemBackgroundRect.anchorMin = Vector2.zero;
                itemBackgroundRect.anchorMax = Vector2.one;
                itemBackgroundRect.offsetMin = Vector2.zero;
                itemBackgroundRect.offsetMax = Vector2.zero;
            }

            RectTransform itemLabelRect = template.Find("Viewport/Content/Item/Item Label") as RectTransform;
            if (itemLabelRect != null)
            {
                itemLabelRect.localScale = Vector3.one;
                itemLabelRect.anchorMin = Vector2.zero;
                itemLabelRect.anchorMax = Vector2.one;
                itemLabelRect.offsetMin = new Vector2(18f, 0f);
                itemLabelRect.offsetMax = new Vector2(-18f, 0f);
            }

            DropdownOptionHoverVisual optionVisual = itemRect != null
                ? GetOrAddComponent<DropdownOptionHoverVisual>(itemRect.gameObject)
                : null;

            Image[] images = template.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image.name == "Item Background")
                {
                    image.color = new Color(0f, 0f, 0f, 0.42f);
                    image.raycastTarget = true;
                }
                else if (image.name == "Item Checkmark")
                {
                    image.sprite = null;
                    image.color = new Color(1f, 1f, 1f, 0.24f);
                    image.raycastTarget = false;
                    StretchToFill(image.transform as RectTransform);
                }
                else if (image.name == "Arrow")
                {
                    image.color = Color.white;
                    image.raycastTarget = false;
                }
            }

            TMP_Text[] texts = template.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
            {
                text.color = Color.white;
                text.enableAutoSizing = true;
                text.fontSizeMin = 18f;
                text.fontSizeMax = 28f;
                ApplyFont(fontSource, text);
            }

            ScrollRect scrollRect = template.GetComponent<ScrollRect>();
            if (scrollRect != null)
            {
                scrollRect.horizontal = false;
                scrollRect.vertical = true;
                scrollRect.movementType = ScrollRect.MovementType.Clamped;
                scrollRect.scrollSensitivity = 30f;
                scrollRect.viewport = viewportRect;
                scrollRect.content = contentRect;
                StyleScrollRect(scrollRect);
            }

            Toggle[] toggles = template.GetComponentsInChildren<Toggle>(true);
            foreach (Toggle toggle in toggles)
            {
                Image itemBackground = toggle.transform.Find("Item Background")?.GetComponent<Image>();
                Image activeHighlight = toggle.transform.Find("Item Checkmark")?.GetComponent<Image>();

                if (itemBackground != null)
                {
                    toggle.targetGraphic = itemBackground;
                }

                if (activeHighlight != null)
                {
                    activeHighlight.sprite = null;
                    activeHighlight.color = new Color(1f, 1f, 1f, 0.24f);
                    activeHighlight.raycastTarget = false;
                    StretchToFill(activeHighlight.transform as RectTransform);
                    toggle.graphic = activeHighlight;
                }

                toggle.toggleTransition = Toggle.ToggleTransition.Fade;

                ColorBlock colors = toggle.colors;
                colors.normalColor = new Color(0f, 0f, 0f, 0.42f);
                colors.highlightedColor = new Color(1f, 1f, 1f, 0.22f);
                colors.pressedColor = new Color(1f, 1f, 1f, 0.34f);
                colors.selectedColor = new Color(1f, 1f, 1f, 0.26f);
                colors.disabledColor = new Color(0f, 0f, 0f, 0.35f);
                colors.colorMultiplier = 1f;
                colors.fadeDuration = 0.08f;
                toggle.colors = colors;

                optionVisual?.Construct(itemBackground, activeHighlight, toggle);
            }
        }

        private static void StyleScrollRect(ScrollRect scrollRect)
        {
            if (scrollRect == null)
            {
                return;
            }

            StyleScrollbar(scrollRect.verticalScrollbar);
            StyleScrollbar(scrollRect.horizontalScrollbar);
        }

        private static void StyleScrollbar(Scrollbar scrollbar)
        {
            if (scrollbar == null)
            {
                return;
            }

            RectTransform scrollbarRect = scrollbar.transform as RectTransform;
            if (scrollbarRect != null)
            {
                if (scrollbar.direction == Scrollbar.Direction.BottomToTop ||
                    scrollbar.direction == Scrollbar.Direction.TopToBottom)
                {
                    scrollbarRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 8f);
                }
                else
                {
                    scrollbarRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 8f);
                }
            }

            Image scrollbarImage = scrollbar.GetComponent<Image>();
            if (scrollbarImage != null)
            {
                scrollbarImage.color = new Color(0f, 0f, 0f, 0.38f);
                scrollbarImage.raycastTarget = false;
            }

            Image[] images = scrollbar.GetComponentsInChildren<Image>(true);
            foreach (Image image in images)
            {
                if (image.transform == scrollbar.transform)
                {
                    continue;
                }

                if (image.name == "Handle")
                {
                    image.color = new Color(1f, 1f, 1f, 0.92f);
                    image.raycastTarget = true;
                }
                else
                {
                    image.color = new Color(0f, 0f, 0f, 0.18f);
                    image.raycastTarget = false;
                }
            }

            ColorBlock colors = scrollbar.colors;
            colors.normalColor = new Color(1f, 1f, 1f, 0.92f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(1f, 1f, 1f, 0.72f);
            colors.selectedColor = Color.white;
            colors.disabledColor = new Color(1f, 1f, 1f, 0.25f);
            colors.colorMultiplier = 1f;
            colors.fadeDuration = 0.08f;
            scrollbar.colors = colors;
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

    internal sealed class DropdownOptionHoverVisual : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler
    {
        private static readonly Color NormalColor = new(0f, 0f, 0f, 0.42f);
        private static readonly Color HoverColor = new(1f, 1f, 1f, 0.22f);
        private static readonly Color PressedColor = new(1f, 1f, 1f, 0.34f);
        private static readonly Color SelectedColor = new(1f, 1f, 1f, 0.24f);

        [SerializeField] private Image background;
        [SerializeField] private Image selectedOverlay;
        [SerializeField] private Toggle toggle;

        private bool isHovered;
        private bool isPressed;

        public void Construct(Image background, Image selectedOverlay, Toggle toggle)
        {
            this.background = background;
            this.selectedOverlay = selectedOverlay;
            this.toggle = toggle;
            Apply();
        }

        private void OnEnable()
        {
            if (toggle != null)
            {
                toggle.onValueChanged.AddListener(OnToggleValueChanged);
            }

            Apply();
        }

        private void OnDisable()
        {
            if (toggle != null)
            {
                toggle.onValueChanged.RemoveListener(OnToggleValueChanged);
            }
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            isHovered = true;
            Apply();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            isHovered = false;
            isPressed = false;
            Apply();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            isPressed = true;
            Apply();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            isPressed = false;
            Apply();
        }

        private void OnToggleValueChanged(bool _)
        {
            Apply();
        }

        private void Apply()
        {
            if (background != null)
            {
                background.color = isPressed
                    ? PressedColor
                    : isHovered
                        ? HoverColor
                        : NormalColor;
            }

            if (selectedOverlay != null)
            {
                selectedOverlay.color = SelectedColor;
                selectedOverlay.enabled = toggle != null && toggle.isOn;
            }
        }
    }

}
