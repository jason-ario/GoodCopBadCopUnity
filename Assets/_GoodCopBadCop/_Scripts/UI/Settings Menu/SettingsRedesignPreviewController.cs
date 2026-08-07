using System;
using System.Collections;

using GoodCopBadCop.Input;
using GoodCopBadCop.Settings;
using System.Collections.Generic;
using TMPro;
using R3;

using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GoodCopBadCop.UI.SettingsMenu
{
    /// <summary>
    /// Runtime-only interaction harness for Settings Menu Redesign Preview.
    /// The production menu remains untouched until this layout is approved.
    /// </summary>
    public sealed class SettingsRedesignPreviewController : MonoBehaviour, ISettingsMenuView
    {
        private const string PreferencePrefix = "settings_preview.";
        private const int RowPoolSize = 8;
        private const float FirstRowY = 355f;
        private const float RowSpacing = 67f;
        private static readonly Vector2 RowPosition = new Vector2(-371.25f, FirstRowY);

        private enum Tab { Gameplay, Graphics, Audio, Controls }
        private enum Control { Dropdown, Slider, Rebind }

        private sealed class Setting
        {
            public readonly string Label;
            public readonly string Key;
            public readonly Control Control;
            public readonly string[] Options;
            public readonly GameAction RebindAction;
            public int Index;
            public float Value;
            private readonly int defaultIndex;
            private readonly float defaultValue;

            public Setting(string label, string key, params string[] options)
            {
                Label = label;
                Key = key;
                Control = Control.Dropdown;
                Options = options;
                Index = 0;
                defaultIndex = 0;
            }

            public Setting(string label, string key, float value)
            {
                Label = label;
                Key = key;
                Control = Control.Slider;
                Value = value;
                defaultValue = value;
            }

            public Setting(string label, string key, GameAction rebindAction)
            {
                Label = label;
                Key = key;
                Control = Control.Rebind;
                RebindAction = rebindAction;
            }

            public void ResetToDefault()
            {
                Index = defaultIndex;
                Value = defaultValue;
                if (Control == Control.Rebind) RebindableInput.ResetToDefault(RebindAction);
            }

            public string DisplayValue =>
                Control == Control.Slider ? Value.ToString("0") :
                Control == Control.Rebind ? RebindableInput.GetDisplayName(RebindAction) :
                Options[Mathf.Clamp(Index, 0, Options.Length - 1)];
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
            new Setting("FPS Limit", "fps_limit", "Unlimited", "30", "60", "120", "144"),
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
            new Setting("Voice Chat", "voice_chat_enabled", "Off", "On"),
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
            new Setting("Interact", "interact", GameAction.Interact),
            new Setting("Crouch", "crouch_key", GameAction.Crouch),
            new Setting("Place Object", "place_object", GameAction.PlaceObject),
            new Setting("Throw Object", "throw_object", GameAction.ThrowObject),
            new Setting("Toggle Mask", "toggle_mask", GameAction.ToggleMask),
            new Setting("Open Emotes", "open_emotes", GameAction.OpenEmotes)
        };

        private readonly List<Row> rows = new List<Row>();
        private readonly List<Button> tabButtons = new List<Button>();
        private readonly List<TMP_Text> tabLabels = new List<TMP_Text>();
        private Tab activeTab = Tab.Gameplay;
        private Setting[] activeSettings;
        [SerializeField] private GameObject rowPrefab;
        [SerializeField] private TextButton gameplayTabButton;
        [SerializeField] private TextButton graphicsTabButton;
        [SerializeField] private TextButton audioTabButton;
        [SerializeField] private TextButton controlsTabButton;

        private ScrollRect settingsScrollRect;
        private RectTransform rowsRoot;
        private RectTransform selectedCategory;
        private TMP_Text sectionTitle;
        private TMP_Text saveText;
        private Button saveButton;
        private Button backButton;
        private Button restoreDefaultButton;
        private ConfirmationDialogController confirmationDialog;
        private readonly Subject<int> displayModeChanged = new Subject<int>();
        private readonly Subject<int> screenResolutionChanged = new Subject<int>();
        private readonly Subject<bool> vSyncChanged = new Subject<bool>();
        private readonly Subject<int> fpsLimitChanged = new Subject<int>();
        private readonly Subject<float> mouseSensitivityChanged = new Subject<float>();
        private readonly Subject<bool> invertYAxisChanged = new Subject<bool>();
        private readonly Subject<int> crouchModeChanged = new Subject<int>();
        private readonly Subject<int> sprintModeChanged = new Subject<int>();
        private readonly Subject<bool> voiceChatEnabledChanged = new Subject<bool>();
        private readonly Subject<bool> voiceChatMutedChanged = new Subject<bool>();
        private readonly Subject<bool> voiceChatDeafenedChanged = new Subject<bool>();
        private readonly Subject<int> voiceChatInputModeChanged = new Subject<int>();
        private readonly Subject<ESettingsMenuTab> tabSelected = new Subject<ESettingsMenuTab>();
        private readonly Subject<Unit> backRequested = new Subject<Unit>();
        private readonly Subject<Unit> closed = new Subject<Unit>();
        private bool isInitialized;
        private bool isCloseRequested;

        public Observable<int> DisplayModeChanged { get { return displayModeChanged; } }
        public Observable<int> ScreenResolutionChanged { get { return screenResolutionChanged; } }
        public Observable<bool> VSyncChanged { get { return vSyncChanged; } }
        public Observable<int> FpsLimitChanged { get { return fpsLimitChanged; } }
        public Observable<float> MouseSensitivityChanged { get { return mouseSensitivityChanged; } }
        public Observable<bool> InvertYAxisChanged { get { return invertYAxisChanged; } }
        public Observable<int> CrouchModeChanged { get { return crouchModeChanged; } }
        public Observable<int> SprintModeChanged { get { return sprintModeChanged; } }
        public Observable<bool> VoiceChatEnabledChanged { get { return voiceChatEnabledChanged; } }
        public Observable<bool> VoiceChatMutedChanged { get { return voiceChatMutedChanged; } }
        public Observable<bool> VoiceChatDeafenedChanged { get { return voiceChatDeafenedChanged; } }
        public Observable<int> VoiceChatInputModeChanged { get { return voiceChatInputModeChanged; } }
        public Observable<ESettingsMenuTab> TabSelected { get { return tabSelected; } }
        public Observable<Unit> BackRequested { get { return backRequested; } }
        public Observable<Unit> Closed { get { return closed; } }

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
        private int _awaitingRebindIndex = -1;

        private void Awake()
        {
            Initialize();
        }

        private void OnEnable()
        {
            isCloseRequested = false;

            if (isInitialized)
            {
                tabSelected.OnNext(ToSettingsMenuTab(activeTab));
            }
        }

        private void Update()
        {
            if (_awaitingRebindIndex >= 0)
            {
                CaptureRebindInput();
                return;
            }

            UnityEngine.InputSystem.Keyboard keyboard = UnityEngine.InputSystem.Keyboard.current;
            bool closePressed = keyboard != null && keyboard.escapeKey.wasPressedThisFrame;

            if (closePressed || UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                RequestClose();
            }
        }

        private void BeginRebind(int index)
        {
            if (index >= activeSettings.Length || activeSettings[index].Control != Control.Rebind) return;
            CloseDropdown();
            _awaitingRebindIndex = index;
            rows[index].Value.text = "Press any key/button…";
        }

        private void CaptureRebindInput()
        {
            if (_awaitingRebindIndex < 0 || _awaitingRebindIndex >= activeSettings.Length)
            {
                _awaitingRebindIndex = -1;
                return;
            }

            Setting setting = activeSettings[_awaitingRebindIndex];

            if (UnityEngine.Input.GetKeyDown(KeyCode.Escape))
            {
                CancelRebind();
                return;
            }

            if (UnityEngine.Input.GetMouseButtonDown(0)) { CompleteMouseRebind(setting, 0); return; }
            if (UnityEngine.Input.GetMouseButtonDown(1)) { CompleteMouseRebind(setting, 1); return; }
            if (UnityEngine.Input.GetMouseButtonDown(2)) { CompleteMouseRebind(setting, 2); return; }

            foreach (KeyCode keyCode in Enum.GetValues(typeof(KeyCode)))
            {
                if (keyCode == KeyCode.Escape) continue;
                if ((int)keyCode >= (int)KeyCode.Mouse0 && (int)keyCode <= (int)KeyCode.Mouse6) continue;
                if (UnityEngine.Input.GetKeyDown(keyCode))
                {
                    CompleteKeyRebind(setting, keyCode);
                    return;
                }
            }
        }

        private void CompleteKeyRebind(Setting setting, KeyCode keyCode)
        {
            if (RebindableInput.HasKeyBinding(setting.RebindAction))
                RebindableInput.SetKey(setting.RebindAction, keyCode);
            else if (RebindableInput.HasMouseBinding(setting.RebindAction))
                return; // this action isn't keyboard-rebindable; ignore key presses.

            FinishRebind();
        }

        private void CompleteMouseRebind(Setting setting, int button)
        {
            if (RebindableInput.HasMouseBinding(setting.RebindAction))
                RebindableInput.SetMouseButton(setting.RebindAction, button);
            else if (RebindableInput.HasKeyBinding(setting.RebindAction))
                return; // this action isn't mouse-rebindable; ignore mouse clicks.

            FinishRebind();
        }

        private void CancelRebind()
        {
            int index = _awaitingRebindIndex;
            _awaitingRebindIndex = -1;
            if (index >= 0 && index < rows.Count) BindRow(index);
        }

        private void FinishRebind()
        {
            int index = _awaitingRebindIndex;
            _awaitingRebindIndex = -1;
            if (index >= 0 && index < rows.Count) BindRow(index);
        }

        private void OnDisable()
        {
            closed.OnNext(Unit.Default);
        }

        public void Initialize()
        {
            if (isInitialized) return;
            isInitialized = true;
            EnsureEventSystem();
            CacheHierarchy();
            BindTabs();
            BindFooter();
            SelectTab(Tab.Gameplay);
            SelectRow(-1);
        }

        private void CacheHierarchy()
        {
            selectedCategory = transform.Find("Selected Category") as RectTransform;
            sectionTitle = transform.Find("Section Title")?.GetComponent<TMP_Text>();
            saveText = transform.Find("Save Button/Save Label")?.GetComponent<TMP_Text>();
            saveButton = GetOrAddButton(transform.Find("Buttons/Save Button")?.gameObject);
            restoreDefaultButton = GetOrAddButton(transform.Find("Buttons/Restore Default")?.gameObject);
            backButton = GetOrAddButton(transform.Find("Back button/Back")?.gameObject);
            confirmationDialog = GetComponentInChildren<ConfirmationDialogController>(true);

            rowsRoot = transform.Find("Settings Viewport/Settings Rows") as RectTransform;
            settingsScrollRect = rowsRoot != null ? rowsRoot.GetComponentInParent<ScrollRect>() : null;
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
                button.onClick.AddListener(() => RequestTab((Tab)index));
                tabLabels.Add(label);
                tabButtons.Add(button);
            }
        }

        private void BindFooter()
        {
            if (saveButton != null) saveButton.onClick.AddListener(Save);
            if (restoreDefaultButton != null) restoreDefaultButton.onClick.AddListener(RequestRestoreDefaults);

            BindCloseButton(backButton);
            BindCloseButton(GetOrAddButton(transform.Find("Back button/Esc")?.gameObject));
            BindCloseButton(GetOrAddButton(transform.Find("Back button/Esc Ring")?.gameObject));
        }

        private void BindCloseButton(Button button)
        {
            if (button == null)
            {
                return;
            }

            Graphic graphic = button.GetComponent<Graphic>();
            if (graphic != null)
            {
                graphic.raycastTarget = true;
                button.targetGraphic = graphic;
            }

            button.onClick.AddListener(RequestClose);
        }

        public void SetVisible(bool isVisible) { gameObject.SetActive(isVisible); }
        public void ShowTab(ESettingsMenuTab tab) { SelectTab(ToInternalTab(tab)); }
        public void SetDisplayModeValue(int value)
        {
            Graphics[0].Index = value == 1 ? 0 : value == 0 ? 1 : 2;
            RefreshActiveSetting("display_mode");
        }
        public void SetScreenResolutionValue(int value) { Graphics[1].Index = value; RefreshActiveSetting("resolution"); }
        public void SetVSyncValue(bool value) { Graphics[2].Index = value ? 1 : 0; RefreshActiveSetting("vsync"); }
        public void SetFpsLimitValue(int value) { Graphics[3].Index = value; RefreshActiveSetting("fps_limit"); }
        public void SetMouseSensitivityValue(float value) { Controls[0].Value = value; RefreshActiveSetting("mouse_sensitivity"); }
        public void SetInvertYAxisValue(bool value) { Controls[1].Index = value ? 1 : 0; RefreshActiveSetting("invert_y"); }
        public void SetCrouchModeValue(int value) { Controls[2].Index = value; RefreshActiveSetting("crouch_mode"); }
        public void SetSprintModeValue(int value) { Controls[3].Index = value; RefreshActiveSetting("sprint_mode"); }
        public void SetVoiceChatEnabledValue(bool value) { Audio[4].Index = value ? 1 : 0; RefreshActiveSetting("voice_chat_enabled"); }
        public void SetVoiceChatMutedValue(bool value) { Audio[6].Index = value ? 1 : 0; RefreshActiveSetting("microphone_muted"); }
        public void SetVoiceChatDeafenedValue(bool value) { Audio[7].Index = value ? 1 : 0; RefreshActiveSetting("voice_deafened"); }
        public void SetVoiceChatInputModeValue(int value) { Audio[5].Index = value; RefreshActiveSetting("voice_input"); }

        private void RequestTab(Tab tab) { tabSelected.OnNext(ToSettingsMenuTab(tab)); }

        private void UpdateTabButtonStates(int activeIndex)
        {
            TextButton[] tabs = { gameplayTabButton, graphicsTabButton, audioTabButton, controlsTabButton };
            for (int i = 0; i < tabs.Length; i++)
            {
                if (tabs[i] == null) continue;
                if (i == activeIndex)
                    tabs[i].SetActiveTab(true);
                else
                    tabs[i].Reset();
            }
        }

        public void OpenGameplayTab()  { UpdateTabButtonStates(0); RequestTab(Tab.Gameplay); }
        public void OpenGraphicsTab()  { UpdateTabButtonStates(1); RequestTab(Tab.Graphics); }
        public void OpenAudioTab()     { UpdateTabButtonStates(2); RequestTab(Tab.Audio); }
        public void OpenControlsTab()  { UpdateTabButtonStates(3); RequestTab(Tab.Controls); }
        private static ESettingsMenuTab ToSettingsMenuTab(Tab tab) { return (ESettingsMenuTab)(int)tab; }
        private static Tab ToInternalTab(ESettingsMenuTab tab) { return (Tab)(int)tab; }
        private void RefreshActiveSetting(string key)
        {
            if (activeSettings == null) return;
            for (int i = 0; i < activeSettings.Length && i < rows.Count; i++)
                if (activeSettings[i].Key == key) { BindRow(i); return; }
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

            UpdateTabButtonStates((int)tab);
        }

        private void BindRow(int index)
        {
            Row row = rows[index];
            Setting setting = activeSettings[index];
            bool supported = IsSupported(setting);
            bool slider = setting.Control == Control.Slider;
            bool rebind = setting.Control == Control.Rebind;

            row.Label.text = setting.Label;
            row.Value.text = index == _awaitingRebindIndex ? "Press any key/button…" : setting.DisplayValue;
            EnsureRowVisuals(row, slider);

            if (row.Button != null)
            {
                row.Button.transition = Selectable.Transition.None;
                row.Button.interactable = supported;
                row.Button.onClick.RemoveAllListeners();
                if (supported && setting.Control == Control.Dropdown)
                {
                    row.Button.onClick.AddListener(() => ToggleDropdown(index));
                }
                else if (supported && rebind)
                {
                    row.Button.onClick.AddListener(() => BeginRebind(index));
                }
            }

            if (row.Arrow != null)
            {
                row.Arrow.gameObject.SetActive(setting.Control == Control.Dropdown);
                row.Arrow.color = new Color(1f, 1f, 1f, supported ? .5f : .18f);
            }

            if (slider)
            {
                ConfigureSliderTrigger(row, index);
                row.SliderHitArea.gameObject.SetActive(supported);
                UpdateSliderVisual(row, setting.Value);
            }

            ApplyRowAppearance(row, supported);
        }

        private static void ApplyRowAppearance(Row row, bool supported)
        {
            if (row.Background != null)
            {
                row.Background.color = supported
                    ? new Color(1f, 1f, 1f, .48f)
                    : new Color(.36f, .38f, .36f, .28f);
            }

            Color textColor = supported
                ? new Color(.58f, .60f, .58f, 1f)
                : new Color(.42f, .44f, .42f, .55f);
            row.Label.color = textColor;
            row.Value.color = textColor;

            SetGraphicColor(row.Track, supported);
            SetGraphicColor(row.Fill, supported);
            SetGraphicColor(row.Handle, supported);
        }

        private static void SetGraphicColor(RectTransform rectTransform, bool supported)
        {
            if (rectTransform == null)
            {
                return;
            }

            Image image = rectTransform.GetComponent<Image>();
            if (image != null)
            {
                image.color = supported ? Color.white : new Color(.45f, .46f, .45f, .42f);
            }
        }

        private static bool IsSupported(Setting setting)
        {
            if (setting.Control == Control.Rebind) return true;

            switch (setting.Key)
            {
                case "display_mode":
                case "resolution":
                case "vsync":
                case "fps_limit":
                case "voice_chat_enabled":
                case "voice_input":
                case "microphone_muted":
                case "voice_deafened":
                case "mouse_sensitivity":
                case "invert_y":
                case "crouch_mode":
                case "sprint_mode":
                    return true;
                default:
                    return false;
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
            if (row.Track == null ||
                !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    row.Track,
                    eventData.position,
                    eventData.pressEventCamera,
                    out Vector2 local))
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
                GameObject optionObject = new GameObject(
                    "Option " + setting.Options[i],
                    typeof(RectTransform),
                    typeof(CanvasRenderer),
                    typeof(Image),
                    typeof(Button));
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
            settingsScrollRect.vertical = true;

            SettingsPreviewScrollRect previewScrollRect = settingsScrollRect as SettingsPreviewScrollRect;
            if (previewScrollRect != null)
            {
                previewScrollRect.InputLocked = locked;
            }

            Scrollbar scrollbar = settingsScrollRect.verticalScrollbar;
            if (scrollbar != null)
            {
                scrollbar.interactable = !locked;
            }
        }





        private void SelectRow(int index)
        {
            for (int i = 0; i < rows.Count; i++)
            {
                Row row = rows[i];
                if (!row.Root.gameObject.activeSelf || row.Background == null || i >= activeSettings.Length)
                {
                    continue;
                }

                row.Background.sprite = normalBackgroundSprite;
                row.Background.rectTransform.sizeDelta = new Vector2(1110.5f, 56f);
                ApplyRowAppearance(row, IsSupported(activeSettings[i]));
            }
        }

        private void Apply(Setting setting)
        {
            if (!IsSupported(setting)) return;
            switch (setting.Key)
            {
                case "display_mode": displayModeChanged.OnNext(setting.Index == 0 ? 1 : setting.Index == 1 ? 0 : 2); break;
                case "resolution": screenResolutionChanged.OnNext(setting.Index); break;
                case "vsync": vSyncChanged.OnNext(setting.Index == 1); break;
                case "fps_limit": fpsLimitChanged.OnNext(setting.Index); break;
                case "voice_chat_enabled": voiceChatEnabledChanged.OnNext(setting.Index == 1); break;
                case "voice_input": voiceChatInputModeChanged.OnNext(setting.Index); break;
                case "microphone_muted": voiceChatMutedChanged.OnNext(setting.Index == 1); break;
                case "voice_deafened": voiceChatDeafenedChanged.OnNext(setting.Index == 1); break;
                case "mouse_sensitivity": mouseSensitivityChanged.OnNext(setting.Value); break;
                case "invert_y": invertYAxisChanged.OnNext(setting.Index == 1); break;
                case "crouch_mode": crouchModeChanged.OnNext(setting.Index); break;
                case "sprint_mode": sprintModeChanged.OnNext(setting.Index); break;
            }
        }
        private void Save()
        {
            RequestClose();
        }

        public void SaveSettings() => Save();

        public void RequestRestoreDefaults()
        {
            if (confirmationDialog == null) return;
            confirmationDialog.Show(
                "Restore Defaults",
                "Reset all settings to their default values?",
                "Restore",
                "Cancel",
                RestoreAllDefaults);
        }

        private void RestoreAllDefaults()
        {
            Setting[][] allTabs = { Gameplay, Graphics, Audio, Controls };
            foreach (Setting[] tab in allTabs)
                foreach (Setting setting in tab)
                    setting.ResetToDefault();

            foreach (Setting[] tab in allTabs)
                foreach (Setting setting in tab)
                    Apply(setting);

            SelectTab(activeTab);
        }

        private void RequestClose()
        {
            if (!gameObject.activeInHierarchy || isCloseRequested)
            {
                return;
            }

            isCloseRequested = true;
            CloseDropdown();
            backRequested.OnNext(Unit.Default);
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
