using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GoodCopBadCop.UI.SettingsMenu
{
    /// <summary>
    /// Runtime-only interaction harness for Settings Menu Redesign Preview.
    /// The production menu remains untouched until this layout is approved.
    /// </summary>
    public sealed class SettingsRedesignPreviewController : MonoBehaviour
    {
        private const string PreferencePrefix = "settings_preview.";
        private const int RowPoolSize = 8;
        private const float FirstRowY = 355f;
        private const float RowSpacing = 67f;
        private static readonly Vector2 RowPosition = new Vector2(-371.25f, FirstRowY);

        private enum Tab { Gameplay, Graphics, Audio, Controls }
        private enum Control { Dropdown, Slider }

        private sealed class Setting
        {
            public readonly string Label;
            public readonly string Key;
            public readonly Control Control;
            public readonly string[] Options;
            public int Index;
            public float Value;

            public Setting(string label, string key, params string[] options)
            {
                Label = label;
                Key = key;
                Control = Control.Dropdown;
                Options = options;
                Index = 0;
            }

            public Setting(string label, string key, float value)
            {
                Label = label;
                Key = key;
                Control = Control.Slider;
                Value = value;
            }

            public string DisplayValue => Control == Control.Slider ? Value.ToString("0") : Options[Mathf.Clamp(Index, 0, Options.Length - 1)];
        }

        private sealed class Row
        {
            public RectTransform Root;
            public Image Background;
            public TMP_Text Label;
            public TMP_Text Value;
            public Image Arrow;
            public RectTransform Track;
            public RectTransform Fill;
            public RectTransform Handle;
            public Button Button;
            public RectTransform SliderHitArea;
        }

        private static readonly Setting[] Gameplay =
        {
            new Setting("Language", "language", "English", "Russian"),
            new Setting("Subtitles", "subtitles", "Off", "On"),
            new Setting("Camera Shake", "camera_shake", "Off", "On"),
            new Setting("Head Bob", "head_bob", "Off", "On")
        };

        private static readonly Setting[] Graphics =
        {
            new Setting("Display Mode", "display_mode", "Borderless", "Fullscreen", "Windowed"),
            new Setting("Resolution", "resolution", "1920 x 1080", "1600 x 900", "1280 x 720"),
            new Setting("VSync", "vsync", "Off", "On"),
            new Setting("FPS Limit", "fps_limit", "Unlimited", "60", "120", "144"),
            new Setting("Quality Preset", "quality", "Low", "Medium", "High", "Ultra"),
            new Setting("Brightness", "brightness", 50f),
            new Setting("Film Grain", "film_grain", "Off", "On"),
            new Setting("Chromatic Aberration", "chromatic_aberration", "Off", "On")
        };

        private static readonly Setting[] Audio =
        {
            new Setting("Master Volume", "master_volume", 80f),
            new Setting("Music Volume", "music_volume", 70f),
            new Setting("SFX Volume", "sfx_volume", 80f),
            new Setting("Voice Volume", "voice_volume", 80f),
            new Setting("Proximity Chat", "proximity_chat", "Off", "On"),
            new Setting("Voice Input", "voice_input", "Voice Activation", "Push To Talk"),
            new Setting("Microphone Muted", "microphone_muted", "Off", "On"),
            new Setting("Voice Deafened", "voice_deafened", "Off", "On")
        };

        private static readonly Setting[] Controls =
        {
            new Setting("Mouse Sensitivity", "mouse_sensitivity", 50f),
            new Setting("Invert Y Axis", "invert_y", "Off", "On"),
            new Setting("Crouch Mode", "crouch_mode", "Hold", "Toggle"),
            new Setting("Sprint Mode", "sprint_mode", "Hold", "Toggle"),
            new Setting("Move Forward", "move_forward", "W"),
            new Setting("Move Backward", "move_backward", "S"),
            new Setting("Jump", "jump", "Space"),
            new Setting("Interact", "interact", "E")
        };

        private readonly List<Row> rows = new List<Row>();
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<TMP_Text> tabLabels = new List<TMP_Text>();
        private Tab activeTab = Tab.Graphics;
        private Setting[] activeSettings;
        [SerializeField] private GameObject rowPrefab;
        
        private ScrollRect settingsScrollRect;
private RectTransform rowsRoot;
        private RectTransform selectedCategory;
        private TMP_Text sectionTitle;
        private TMP_Text saveText;
        private Button saveButton;
        private Button backButton;
        private Image normalBackground;
        private Sprite selectedBackground;
        
        
        [SerializeField] private Sprite dropdownSelectedSprite;
private Sprite tabHighlightSprite;
private Sprite normalBackgroundSprite;
        private RectTransform trackTemplate;
        private RectTransform fillTemplate;
        private RectTransform handleTemplate;
        

        private const float TabHighlightX = -647.5f;
        private static readonly Vector2 TabHighlightSize = new Vector2(290f, 82.5f);
        private RectTransform dropdownPanel;
        private Row openDropdownRow;
        private int openDropdownIndex = -1;
private Image arrowTemplate;

private void Awake()
        {
            EnsureEventSystem();
            LoadPreferences();
            CacheHierarchy();
            BindTabs();
            BindFooter();
            SelectTab(Tab.Graphics);
            SelectRow(-1);
        }

private static void LoadPreferences()
        {
            foreach (Setting setting in Gameplay) LoadPreference(setting);
            foreach (Setting setting in Graphics) LoadPreference(setting);
            foreach (Setting setting in Audio) LoadPreference(setting);
            foreach (Setting setting in Controls) LoadPreference(setting);
        }

        private static void LoadPreference(Setting setting)
        {
            if (setting.Control == Control.Slider)
            {
                setting.Value = PlayerPrefs.GetFloat(PreferencePrefix + setting.Key, setting.Value);
            }
            else
            {
                setting.Index = PlayerPrefs.GetInt(PreferencePrefix + setting.Key, 0);
            }
        }


private void CacheHierarchy()
        {
            selectedCategory = transform.Find("Selected Category") as RectTransform;
            sectionTitle = transform.Find("Section Title")?.GetComponent<TMP_Text>();
            saveText = transform.Find("Save Button/Save Label")?.GetComponent<TMP_Text>();
            saveButton = GetOrAddButton(transform.Find("Save Button")?.gameObject);
            backButton = GetOrAddButton(transform.Find("Back")?.gameObject);
            
            settingsScrollRect = rowsRoot != null ? rowsRoot.GetComponentInParent<ScrollRect>() : null;
rowsRoot = transform.Find("Settings Viewport/Settings Rows") as RectTransform;
            BuildRowPool();
            Row firstRow = rows.Count > 0 ? rows[0] : null;
            selectedBackground = firstRow?.Background != null ? firstRow.Background.sprite : null;
            normalBackgroundSprite = firstRow?.Background != null ? firstRow.Background.sprite : null;
            tabHighlightSprite = selectedCategory != null ? selectedCategory.GetComponent<Image>()?.sprite : null;
            normalBackground = transform.Find("Background Dim")?.GetComponent<Image>();
            Row sliderSource = rows.Find(row => row.Track != null);
            trackTemplate = sliderSource?.Track;
            fillTemplate = sliderSource?.Fill;
            handleTemplate = sliderSource?.Handle;
            EnsureDropdownPanel();
            arrowTemplate = rows.Find(row => row.Arrow != null)?.Arrow;
        }

private void BuildRowPool()
        {
            rows.Clear();
            if (rowsRoot == null || rowPrefab == null)
            {
                Debug.LogError("Settings preview needs the universal Settings Row prefab assigned.", this);
                return;
            }

            int reusableCount = Mathf.Min(rowsRoot.childCount, RowPoolSize);
            for (int i = 0; i < RowPoolSize; i++)
            {
                GameObject rowObject = i < reusableCount
                    ? rowsRoot.GetChild(i).gameObject
                    : Instantiate(rowPrefab, rowsRoot);
                rowObject.name = "Settings Row";
                RectTransform root = rowObject.transform as RectTransform;
                root.sizeDelta = new Vector2(1110.5f, 56f);
                rowObject.SetActive(false);
                rows.Add(CacheRow(root));
            }
        }

        private static Row CacheRow(RectTransform root)
        {
            Row row = new Row
            {
                Root = root,
                Background = root.Find("Background")?.GetComponent<Image>(),
                Label = root.Find("Label")?.GetComponent<TMP_Text>(),
                Value = root.Find("Value")?.GetComponent<TMP_Text>(),
                Arrow = root.Find("Dropdown Arrow")?.GetComponent<Image>(),
                Track = root.Find("Slider Track") as RectTransform,
                Fill = root.Find("Slider Fill") as RectTransform,
                Handle = root.Find("Slider Pointer") as RectTransform
            };
            row.Button = GetOrAddButton(root.gameObject);
            if (row.Button != null && row.Background != null)
            {
                row.Background.raycastTarget = true;
                row.Button.targetGraphic = row.Background;
            }
            return row;
        }


private void EnsureDropdownPanel()
        {
            if (dropdownPanel != null)
            {
                return;
            }

            GameObject panelObject = new GameObject("Dropdown Popup", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            dropdownPanel = panelObject.GetComponent<RectTransform>();
            dropdownPanel.SetParent(transform, false);
            dropdownPanel.anchorMin = new Vector2(.5f, .5f);
            dropdownPanel.anchorMax = new Vector2(.5f, .5f);
            dropdownPanel.pivot = new Vector2(0f, 1f);

            Image panelImage = panelObject.GetComponent<Image>();
            Sprite opaquePanelSprite = Sprite.Create(
                Texture2D.whiteTexture,
                new Rect(0f, 0f, Texture2D.whiteTexture.width, Texture2D.whiteTexture.height),
                new Vector2(.5f, .5f));
            panelImage.sprite = opaquePanelSprite;
            panelImage.type = Image.Type.Simple;
            panelImage.color = Color.black;
            panelImage.raycastTarget = true;
            panelObject.SetActive(false);
        }


private void BindTabs()
        {
            string[] names = { "Gameplay", "Graphics", "Audio", "Controls" };
            for (int i = 0; i < names.Length; i++)
            {
                TMP_Text label = transform.Find("Tab " + names[i])?.GetComponent<TMP_Text>();
                if (label == null)
                {
                    continue;
                }

                label.raycastTarget = true;
                Button button = GetOrAddButton(label.gameObject);
                button.transition = Selectable.Transition.None;
                button.targetGraphic = label;
                int index = i;
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => SelectTab((Tab)index));
                tabLabels.Add(label);
                tabButtons.Add(button);
            }
        }

        private void BindFooter()
        {
            if (saveButton != null)
            {
                saveButton.onClick.AddListener(Save);
            }

            if (backButton != null)
            {
                backButton.onClick.AddListener(() => gameObject.SetActive(false));
            }
        }

private void SelectTab(Tab tab)
        {
            CloseDropdown();
            activeTab = tab;
            activeSettings = GetSettings(tab);
            if (sectionTitle != null)
            {
                sectionTitle.text = tab.ToString();
            }

            for (int i = 0; i < tabLabels.Count; i++)
            {
                bool selected = i == (int)tab;
                tabLabels[i].color = selected ? Color.white : new Color(.53f, .55f, .53f, 1f);
                if (selectedCategory != null && selected)
                {
                    RectTransform labelRect = tabLabels[i].transform as RectTransform;
                    if (labelRect != null)
                    {
                        selectedCategory.sizeDelta = TabHighlightSize;
                        selectedCategory.anchoredPosition = new Vector2(TabHighlightX, labelRect.anchoredPosition.y);
                    }
                }
            }

            for (int i = 0; i < rows.Count; i++)
            {
                bool visible = i < activeSettings.Length;
                if (visible)
                {
                    rows[i].Root.gameObject.SetActive(true);
                    BindRow(i);
                }
                else
                {
                    rows[i].Label.text = string.Empty;
                    rows[i].Value.text = string.Empty;
                    rows[i].Root.gameObject.SetActive(false);
                }
            }

            if (rowsRoot != null)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rowsRoot);
                ScrollRect scrollRect = rowsRoot.GetComponentInParent<ScrollRect>();
                if (scrollRect != null)
                {
                    scrollRect.Rebuild(CanvasUpdate.PostLayout);
                    scrollRect.verticalNormalizedPosition = 1f;
                }
            }
        }

private void BindRow(int index)
        {
            Row row = rows[index];
            Setting setting = activeSettings[index];
            row.Label.text = setting.Label;
            row.Value.text = setting.DisplayValue;

            if (row.Button != null)
            {
                row.Button.transition = Selectable.Transition.None;
                row.Button.onClick.RemoveAllListeners();
                if (setting.Control == Control.Dropdown)
                {
                    row.Button.onClick.AddListener(() => ToggleDropdown(index));
                }
            }

            bool slider = setting.Control == Control.Slider;
            EnsureRowVisuals(row, slider);
            if (row.Arrow != null)
            {
                row.Arrow.gameObject.SetActive(!slider);
                row.Arrow.color = new Color(1f, 1f, 1f, .5f);
            }

            if (slider)
            {
                ConfigureSliderTrigger(row, index);
                UpdateSliderVisual(row, setting.Value);
            }
        }

        private void EnsureRowVisuals(Row row, bool slider)
        {
            if (slider && row.Track == null && trackTemplate != null)
            {
                row.Track = Instantiate(trackTemplate, row.Root);
                row.Track.name = "Slider Track";
                row.Fill = Instantiate(fillTemplate, row.Root);
                row.Fill.name = "Slider Fill";
                row.Handle = Instantiate(handleTemplate, row.Root);
                row.Handle.name = "Slider Pointer";
            }

            if (!slider && row.Arrow == null && arrowTemplate != null)
            {
                row.Arrow = Instantiate(arrowTemplate, row.Root);
                row.Arrow.name = "Dropdown Arrow";
            }

            if (row.Track != null)
            {
                row.Track.gameObject.SetActive(slider);
            }
            if (row.Fill != null)
            {
                row.Fill.gameObject.SetActive(slider);
            }
            
            if (row.SliderHitArea != null)
            {
                row.SliderHitArea.gameObject.SetActive(slider);
            }
if (row.Handle != null)
            {
                row.Handle.gameObject.SetActive(slider);
            }
        }

private void ConfigureSliderTrigger(Row row, int index)
        {
            if (row.Track == null)
            {
                return;
            }

            if (row.SliderHitArea == null)
            {
                GameObject hitAreaObject = new GameObject(
                    "Slider Hit Area",
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(SettingsPreviewSliderHitArea));
                row.SliderHitArea = hitAreaObject.GetComponent<RectTransform>();
                row.SliderHitArea.SetParent(row.Root, false);

                Image hitAreaImage = hitAreaObject.GetComponent<Image>();
                hitAreaImage.color = new Color(1f, 1f, 1f, .001f);
                hitAreaImage.raycastTarget = true;
            }

            row.SliderHitArea.anchorMin = row.Track.anchorMin;
            row.SliderHitArea.anchorMax = row.Track.anchorMax;
            row.SliderHitArea.pivot = row.Track.pivot;
            row.SliderHitArea.anchoredPosition = row.Track.anchoredPosition;
            row.SliderHitArea.sizeDelta = new Vector2(row.Track.rect.width, 44f);
            row.SliderHitArea.SetAsLastSibling();
            row.SliderHitArea.gameObject.SetActive(true);

            SettingsPreviewSliderHitArea receiver = row.SliderHitArea.GetComponent<SettingsPreviewSliderHitArea>();
            receiver.PointerChanged = data => SetSliderFromPointer(index, data);
        }

        private static void AddEvent(EventTrigger trigger, EventTriggerType type, Action<BaseEventData> callback)
        {
            EventTrigger.Entry entry = new EventTrigger.Entry { eventID = type };
            entry.callback.AddListener(data => callback(data));
            trigger.triggers.Add(entry);
        }

        private void SetSliderFromPointer(int index, PointerEventData eventData)
        {
            if (eventData == null || index >= activeSettings.Length)
            {
                return;
            }

            Row row = rows[index];
            if (row.Track == null || !RectTransformUtility.ScreenPointToLocalPointInRectangle(row.Track, eventData.position, eventData.pressEventCamera, out Vector2 local))
            {
                return;
            }

            float normalized = Mathf.Clamp01(local.x / row.Track.rect.width);
            activeSettings[index].Value = Mathf.Round(normalized * 100f);
            row.Value.text = activeSettings[index].DisplayValue;
            UpdateSliderVisual(row, activeSettings[index].Value);
            Apply(activeSettings[index]);
        }

private void UpdateSliderVisual(Row row, float value)
        {
            if (row.Track == null || row.Fill == null || row.Handle == null)
            {
                return;
            }

            float normalized = Mathf.Clamp01(value / 100f);
            float width = row.Track.rect.width;
            float leftX = row.Track.anchoredPosition.x - width * row.Track.pivot.x;

            row.Fill.anchorMin = row.Track.anchorMin;
            row.Fill.anchorMax = row.Track.anchorMax;
            row.Fill.pivot = new Vector2(0f, .5f);
            row.Fill.anchoredPosition = new Vector2(leftX, row.Track.anchoredPosition.y);
            row.Fill.sizeDelta = new Vector2(width * normalized + 5f, row.Track.rect.height);

            Vector2 handlePosition = row.Handle.anchoredPosition;
            handlePosition.x = leftX + width * normalized;
            handlePosition.y = row.Track.anchoredPosition.y;
            row.Handle.anchoredPosition = handlePosition;
        }

private void ToggleDropdown(int index)
        {
            if (index >= activeSettings.Length || activeSettings[index].Control != Control.Dropdown)
            {
                return;
            }

            if (openDropdownIndex == index)
            {
                CloseDropdown();
                return;
            }

            CloseDropdown();
            ShowDropdown(index);
        }

private void ShowDropdown(int index)
        {
            Setting setting = activeSettings[index];
            openDropdownIndex = index;
            openDropdownRow = rows[index];

            
            SetScrollingLocked(true);
dropdownPanel.gameObject.SetActive(true);
            dropdownPanel.SetAsLastSibling();
            dropdownPanel.sizeDelta = new Vector2(360f, setting.Options.Length * 48f + 12f);

            Vector2 localPosition;
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(null, openDropdownRow.Value.rectTransform.position);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                transform as RectTransform,
                screenPosition,
                null,
                out localPosition);
            dropdownPanel.anchoredPosition = new Vector2(
                localPosition.x - 12f,
                localPosition.y - openDropdownRow.Root.rect.height * .5f);

            for (int i = 0; i < setting.Options.Length; i++)
            {
                int optionIndex = i;
                GameObject optionObject = new GameObject("Option " + setting.Options[i], typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
                RectTransform optionRect = optionObject.GetComponent<RectTransform>();
                optionRect.SetParent(dropdownPanel, false);
                optionRect.anchorMin = new Vector2(0f, 1f);
                optionRect.anchorMax = new Vector2(1f, 1f);
                optionRect.pivot = new Vector2(.5f, 1f);
                optionRect.sizeDelta = new Vector2(-12f, 44f);
                optionRect.anchoredPosition = new Vector2(0f, -6f - i * 48f);

                Image optionImage = optionObject.GetComponent<Image>();
                optionImage.sprite = i == setting.Index
                    ? (dropdownSelectedSprite != null ? dropdownSelectedSprite : normalBackgroundSprite)
                    : normalBackgroundSprite;
                optionImage.color = Color.white;

                Button optionButton = optionObject.GetComponent<Button>();
                optionButton.targetGraphic = optionImage;
                optionButton.onClick.AddListener(() => SelectDropdownOption(index, optionIndex));

                GameObject labelObject = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                RectTransform labelRect = labelObject.GetComponent<RectTransform>();
                labelRect.SetParent(optionRect, false);
                labelRect.anchorMin = Vector2.zero;
                labelRect.anchorMax = Vector2.one;
                labelRect.offsetMin = new Vector2(20f, 0f);
                labelRect.offsetMax = new Vector2(-20f, 0f);

                TextMeshProUGUI label = labelObject.GetComponent<TextMeshProUGUI>();
                label.font = openDropdownRow.Value.font;
                label.fontSize = openDropdownRow.Value.fontSize;
                label.fontStyle = openDropdownRow.Value.fontStyle;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.color = i == setting.Index ? Color.white : new Color(.75f, .77f, .75f, 1f);
                label.raycastTarget = false;
                label.text = setting.Options[i];
            }
        }

private void SelectDropdownOption(int settingIndex, int optionIndex)
        {
            Setting setting = activeSettings[settingIndex];
            setting.Index = optionIndex;
            rows[settingIndex].Value.text = setting.DisplayValue;
            Apply(setting);
            CloseDropdown();
        }

        private void CloseDropdown()
        {
            if (dropdownPanel == null)
            {
                return;
            }

            for (int i = dropdownPanel.childCount - 1; i >= 0; i--)
            {
                Destroy(dropdownPanel.GetChild(i).gameObject);
            }

            
            SetScrollingLocked(false);
dropdownPanel.gameObject.SetActive(false);
            openDropdownRow = null;
            openDropdownIndex = -1;
        }

private void SetScrollingLocked(bool locked)
        {
            if (settingsScrollRect == null && rowsRoot != null)
            {
                settingsScrollRect = rowsRoot.GetComponentInParent<ScrollRect>();
            }

            if (settingsScrollRect == null)
            {
                return;
            }

            settingsScrollRect.velocity = Vector2.zero;
            settingsScrollRect.vertical = !locked;

            Scrollbar scrollbar = settingsScrollRect.verticalScrollbar;
            if (scrollbar != null)
            {
                scrollbar.interactable = !locked;
            }
        }





private void SelectRow(int index)
        {
            // Category selection and setting-row focus are independent.
            // No row is enlarged merely because a category or dropdown is opened.
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (!row.Root.gameObject.activeSelf || row.Background == null)
                {
                    continue;
                }

                row.Background.sprite = normalBackgroundSprite;
                row.Background.color = new Color(1f, 1f, 1f, .48f);
                row.Background.rectTransform.sizeDelta = new Vector2(1110.5f, 56f);
                row.Label.color = new Color(.56f, .58f, .56f, 1f);
                row.Value.color = new Color(.58f, .60f, .58f, 1f);
            }
        }

        private void Apply(Setting setting)
        {
            switch (setting.Key)
            {
                case "display_mode":
                    Screen.fullScreenMode = setting.Index == 0 ? FullScreenMode.FullScreenWindow : setting.Index == 1 ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed;
                    break;
                case "vsync":
                    QualitySettings.vSyncCount = setting.Index;
                    break;
                case "fps_limit":
                    Application.targetFrameRate = setting.Index == 0 ? -1 : int.Parse(setting.DisplayValue);
                    break;
                case "master_volume":
                    AudioListener.volume = setting.Value / 100f;
                    break;
                case "brightness":
                    if (normalBackground != null)
                    {
                        Color color = normalBackground.color;
                        color.a = Mathf.Lerp(.68f, .18f, setting.Value / 100f);
                        normalBackground.color = color;
                    }
                    break;
            }
        }

        private void Save()
        {
            CloseDropdown();
            foreach (Setting setting in Gameplay) SaveSetting(setting);
            foreach (Setting setting in Graphics) SaveSetting(setting);
            foreach (Setting setting in Audio) SaveSetting(setting);
            foreach (Setting setting in Controls) SaveSetting(setting);
            PlayerPrefs.Save();
            StartCoroutine(ShowSaved());
        }

        private static void SaveSetting(Setting setting)
        {
            if (setting.Control == Control.Slider)
            {
                PlayerPrefs.SetFloat(PreferencePrefix + setting.Key, setting.Value);
            }
            else
            {
                PlayerPrefs.SetInt(PreferencePrefix + setting.Key, setting.Index);
            }
        }

        private IEnumerator ShowSaved()
        {
            if (saveText == null)
            {
                yield break;
            }

            saveText.text = "Saved";
            yield return new WaitForSecondsRealtime(.75f);
            saveText.text = "Save";
        }

        private static Setting[] GetSettings(Tab tab)
        {
            switch (tab)
            {
                case Tab.Gameplay: return Gameplay;
                case Tab.Audio: return Audio;
                case Tab.Controls: return Controls;
                default: return Graphics;
            }
        }

        private static Button GetOrAddButton(GameObject target)
        {
            return target == null ? null : target.GetComponent<Button>() ?? target.AddComponent<Button>();
        }

        private static void EnsureEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            }
        }
    }
}