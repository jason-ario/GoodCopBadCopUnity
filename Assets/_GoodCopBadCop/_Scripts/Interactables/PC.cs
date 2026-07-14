using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DG.Tweening;
using GoodCopBadCop.Population;
using GoodCopBadCop.SuspectPaperwork;
using TMPro;
using UnityEngine;

public class PC : Interactable
{
    private enum TerminalSection
    {
        All,
        Residents,
        Deceased,
        Quarantine,
        News
    }

    private enum MainMenuMode
    {
        Root,
        Registry
    }

    private enum TerminalScreen
    {
        RootMenu,
        RegistryMenu,
        List,
        Profile,
        NewsEntry
    }

    private struct TerminalNavigationState
    {
        public TerminalScreen Screen;
        public MainMenuMode MenuMode;
        public TerminalSection Section;
        public SuspectData ProfileSuspect;
        public TerminalNewsEntry NewsEntry;

        public static TerminalNavigationState Root()
        {
            return new TerminalNavigationState
            {
                Screen = TerminalScreen.RootMenu,
                MenuMode = MainMenuMode.Root,
                Section = TerminalSection.All
            };
        }
    }

    private const string MainMenuVillageName = "Saplavi";
    private const string MainMenuPopulationObjectName = "Main Menu Population";
    private const string UpDirectoryButtonName = "Up Directory (1)";
    private const string ProfileUpDirectoryButtonName = "Up Directory";
    private const int DefaultTerminalPopulation = 300;
    private static readonly Vector2 MainMenuTitlePosition = new Vector2(-0.056f, 1.168f);
    private static readonly Vector2 MainMenuPopulationPosition = new Vector2(0f, -0.85f);

    [Header("Data")]
    [SerializeField] private SuspectSet _suspectSet;
    [SerializeField] private NewspaperContentScriptable[] _newspaperContentScriptables;

    [Header("Set Up")]
    [SerializeField] private GameObject computerCamera;
    [SerializeField] private Transform lookAtTarget;
    [SerializeField] private Transform standPos;
    private bool pcActive = false;
    private PlayerInteractionController _player;
    [SerializeField] private SimpleCanvasCursorFromMouseDelta _virtualCanvasCursor;

    [Header("Screens")]
    [SerializeField] private GameObject mainScreen;
    [SerializeField] private GameObject suspectListScreen;
    [SerializeField] private ProfilePage profilePage;

    [Header("Optional UI Renderer")]
    [SerializeField] private TerminalRecordListUI terminalRecordListUI;

    [SerializeField] private SimpleCanvasCursorFromMouseDelta mouseCursor;
    [SerializeField] private ClickablePCScrollbar PCScrollbar;

    [VContainer.Inject] private IPopulationModel _populationModel;

    // Profile navigation state
    private SuspectData _currentProfileSuspect;
    private int _currentProfileIndex = -1;
    [SerializeField] private PCFolderTab[] _folderTabs;
    private bool isOn;

    // List management
    private List<SuspectData> _currentBaseList;
    private List<SuspectData> _currentVisibleList;
    private List<TerminalNewsEntry> _currentNewsEntries = new();
    private bool _showQuarantineDays;
    private TerminalSection _currentSection = TerminalSection.All;
    private int _debugCurrentDayOverride = -1;
    private TextMeshProUGUI _mainMenuPopulationLabel;
    private ClickablePCElement _upDirectoryButton;
    private ClickablePCElement _profileUpDirectoryButton;
    private Transform _upDirectoryOriginalParent;
    private UnityEngine.Events.UnityAction _upDirectoryBackAction;
    private MainMenuMode _mainMenuMode = MainMenuMode.Root;
    private readonly Stack<TerminalNavigationState> _terminalBackStack = new();
    private TerminalNavigationState _currentNavigationState = TerminalNavigationState.Root();
    private bool _restoringTerminalNavigation;
    private TerminalNewsEntry _currentNewsEntry;
    private static readonly DateTime NewspaperStartDate = new DateTime(1989, 10, 20);

    private void Start()
    {
        CacheUpDirectoryButton();
        CacheProfileDirectoryButton();
        CloseAllScreens();
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        player.playerMovementController.SetCanControl(false);
        player.SetCanInteract(false, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        ShowPCBackButton();

        player.playerMovementController.LookAtTarget(lookAtTarget.transform);
        player.transform.DOMove(standPos.position, 0.5f);
        player.transform.DORotate(standPos.rotation.eulerAngles, 0.5f);

        //Move camera
        player.playerMovementController.MoveCameraTo(computerCamera.transform);

        pcActive = true;
        _player = player;
        _virtualCanvasCursor.enabled = true;

        if (!isOn)
        {
            isOn = true;
            ResetTerminalNavigation();
            OpenScreen(mainScreen);
        }

        ClearCurrentProfileSelection();
    }

    private void Update()
    {
        if (!pcActive) return;

        if (Input.GetButtonDown("Back"))
        {
            HandleBackButton();
        }
    }

    private void ShowPCBackButton()
    {
        if (UIController.Instance == null)
            return;

        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(HandleBackButton);
    }

    private void HandleBackButton()
    {
        if (GoBack())
            return;

        ExitPC();
    }

    private void ExitPC()
    {
        pcActive = false;

        if (UIController.Instance != null)
            UIController.Instance.HideBackButton();

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        if (_virtualCanvasCursor != null)
            _virtualCanvasCursor.enabled = false;

        if (_player == null)
            return;

        _player.SetCanInteract(true, "");
        _player.playerMovementController.ResetCameraPos(false, 0.5f, () => _player.playerMovementController.SetCanControl(true));
    }

    public void OpenScreen(GameObject screen)
    {
        CloseAllScreens();

        suspectListScreen.SetActive(screen == suspectListScreen);
        mainScreen.SetActive(screen == mainScreen);

        if (screen == mainScreen)
            RefreshMainMenu();

        RefreshUpDirectoryButton(screen);
        RefreshMouseNow();
        StartCoroutine(WaitAndRefreshMouse());
    }

    public void DebugSetCurrentDay(int currentDay)
    {
        _debugCurrentDayOverride = currentDay;
    }

    public void DebugOpenTerminal()
    {
        _mainMenuMode = MainMenuMode.Root;
        pcActive = true;
        isOn = true;
        ResetTerminalNavigation();

        if (_virtualCanvasCursor != null)
            _virtualCanvasCursor.enabled = true;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

        ShowPCBackButton();
        OpenScreen(mainScreen);
        RefreshMouseNow();
        ClearCurrentProfileSelection();
    }

    private IEnumerator WaitAndRefreshMouse()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        RefreshMouseNow();
    }

    private void OpenRootMenu()
    {
        _mainMenuMode = MainMenuMode.Root;
        _upDirectoryBackAction = null;
        _currentNavigationState = TerminalNavigationState.Root();
        OpenScreen(mainScreen);
    }

    private void ResetTerminalNavigation()
    {
        _terminalBackStack.Clear();
        _currentNewsEntry = null;
        _currentNavigationState = TerminalNavigationState.Root();
        _upDirectoryBackAction = null;
    }

    private void PrepareForwardNavigation()
    {
        if (!_restoringTerminalNavigation)
            _terminalBackStack.Push(_currentNavigationState);

        _upDirectoryBackAction = () => GoBack();
    }


    private bool GoBack()
    {
        if (_terminalBackStack.Count == 0)
            return false;

        TerminalNavigationState previousState = _terminalBackStack.Pop();
        _restoringTerminalNavigation = true;
        ApplyTerminalNavigationState(previousState);
        _restoringTerminalNavigation = false;
        return true;
    }

    private void ApplyTerminalNavigationState(TerminalNavigationState state)
    {
        switch (state.Screen)
        {
            case TerminalScreen.RegistryMenu:
                OpenRegistry();
                break;
            case TerminalScreen.List:
                OpenSection(state.Section);
                break;
            case TerminalScreen.Profile:
                OpenProfilePage(state.ProfileSuspect, false);
                break;
            case TerminalScreen.NewsEntry:
                OpenNewsEntryPage(state.NewsEntry, false);
                break;
            case TerminalScreen.RootMenu:
            default:
                OpenRootMenu();
                break;
        }
    }

    private void OpenSection(TerminalSection section)
    {
        switch (section)
        {
            case TerminalSection.Residents:
                OpenResidents();
                break;
            case TerminalSection.Deceased:
                OpenDeceased();
                break;
            case TerminalSection.Quarantine:
                OpenQuarantine();
                break;
            case TerminalSection.News:
                OpenNews();
                break;
            case TerminalSection.All:
            default:
                OpenAll();
                break;
        }
    }

    private void SetCurrentListNavigationState()
    {
        _currentNavigationState = new TerminalNavigationState
        {
            Screen = TerminalScreen.List,
            MenuMode = _mainMenuMode,
            Section = _currentSection
        };
    }
    private void RefreshUpDirectoryButton(GameObject screen)
    {
        CacheUpDirectoryButton();
        if (_upDirectoryButton == null)
            return;

        bool visible = _upDirectoryBackAction != null;
        Transform parent = screen == mainScreen && visible
            ? mainScreen.transform
            : _upDirectoryOriginalParent;
        SetUpDirectoryButtonParent(parent);
        ConfigureUpDirectoryButton(visible, _upDirectoryBackAction);
    }

    private void ConfigureUpDirectoryButton(bool visible, UnityEngine.Events.UnityAction onClick)
    {
        if (_upDirectoryButton == null)
            return;

        _upDirectoryButton.gameObject.SetActive(visible);
        _upDirectoryButton.onClickEvent = new UnityEngine.Events.UnityEvent();
        if (onClick != null)
            _upDirectoryButton.onClickEvent.AddListener(onClick);
    }
    private static void ConfigureUpDirectoryButton(ClickablePCElement button, bool visible, UnityEngine.Events.UnityAction onClick)
    {
        if (button == null)
            return;

        button.gameObject.SetActive(visible);
        button.onClickEvent = new UnityEngine.Events.UnityEvent();
        if (onClick != null)
            button.onClickEvent.AddListener(onClick);
    }

    private void RefreshProfileUpDirectoryButton()
    {
        CacheProfileDirectoryButton();
        if (_profileUpDirectoryButton == null)
            return;

        ConfigureUpDirectoryButton(_profileUpDirectoryButton, _upDirectoryBackAction != null, _upDirectoryBackAction);
    }

    private void SetUpDirectoryButtonParent(Transform parent)
    {
        if (_upDirectoryButton == null || parent == null || _upDirectoryButton.transform.parent == parent)
            return;

        _upDirectoryButton.transform.SetParent(parent, false);
    }

    private void CacheUpDirectoryButton()
    {
        if (_upDirectoryButton != null)
            return;

        Transform upDirectory = FindDescendantByName(transform, UpDirectoryButtonName);
        if (upDirectory == null)
            return;

        _upDirectoryButton = upDirectory.GetComponent<ClickablePCElement>();
        _upDirectoryOriginalParent = upDirectory.parent;
    }
    private void CacheProfileDirectoryButton()
    {
        if (_profileUpDirectoryButton != null || profilePage == null)
            return;

        Transform upDirectory = FindDescendantByName(profilePage.transform, ProfileUpDirectoryButtonName);
        if (upDirectory == null)
            return;

        _profileUpDirectoryButton = upDirectory.GetComponent<ClickablePCElement>();
    }

    private static Transform FindDescendantByName(Transform root, string targetName)
    {
        if (root == null)
            return null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            if (child.name == targetName)
                return child;

            Transform result = FindDescendantByName(child, targetName);
            if (result != null)
                return result;
        }

        return null;
    }

    private void RefreshMouseNow()
    {
        if (mouseCursor != null)
            mouseCursor.SetScreenContent();
    }

    public void CloseAllScreens()
    {
        suspectListScreen.SetActive(false);
        mainScreen.SetActive(false);
        profilePage.gameObject.SetActive(false);
    }

    public void OpenProfilePage(SuspectData suspectData)
    {
        OpenProfilePage(suspectData, true);
    }

    private void OpenProfilePage(SuspectData suspectData, bool recordHistory)
    {
        if (suspectData == null)
            return;

        if (recordHistory)
            PrepareForwardNavigation();

        _currentProfileSuspect = suspectData;
        _currentProfileIndex = GetProfileNavigationIndex(suspectData);

        CloseAllScreens();
        profilePage.gameObject.SetActive(true);

        profilePage.SetProfileData(
            suspectData,
            GetProfileEntryReason(suspectData),
            GetProfileLastEntryDate(suspectData),
            GetProfileStatus(suspectData));

        UpdateProfileNavigationUI();
        RefreshProfileUpDirectoryButton();
        _currentNavigationState = new TerminalNavigationState
        {
            Screen = TerminalScreen.Profile,
            MenuMode = _mainMenuMode,
            Section = _currentSection,
            ProfileSuspect = suspectData
        };

        StartCoroutine(WaitAndRefreshMouse());
    }

    private void UpdateProfileNavigationUI()
    {
        if (profilePage == null)
            return;

        profilePage.SetNavigationState(
            CanOpenPreviousProfile(),
            CanOpenNextProfile()
        );
    }

    public void OpenNextProfile()
    {
        if (_currentProfileSuspect == null)
            return;

        if (_currentProfileIndex < 0)
            _currentProfileIndex = GetProfileNavigationIndex(_currentProfileSuspect);

        if (_currentProfileIndex < 0)
            return;

        int nextIndex = _currentProfileIndex + 1;

        List<SuspectData> navigationList = GetProfileNavigationList();
        if (navigationList.Count == 0)
            return;

        if (nextIndex >= navigationList.Count)
            nextIndex = navigationList.Count - 1;

        SuspectData nextSuspect = navigationList[nextIndex];
        if (nextSuspect == null)
            return;

        OpenProfilePage(nextSuspect, false); // already updates UI
    }

    public void OpenPreviousProfile()
    {
        List<SuspectData> navigationList = GetProfileNavigationList();
        if (navigationList.Count == 0)
            return;

        if (_currentProfileSuspect == null)
            return;

        if (_currentProfileIndex < 0)
            _currentProfileIndex = GetProfileNavigationIndex(_currentProfileSuspect);

        if (_currentProfileIndex < 0)
            return;

        int previousIndex = _currentProfileIndex - 1;

        // Clamp to first entry
        if (previousIndex < 0)
            previousIndex = 0;

        SuspectData previousSuspect = navigationList[previousIndex];
        if (previousSuspect == null)
            return;

        OpenProfilePage(previousSuspect, false);
    }

    public bool CanOpenNextProfile()
    {
        List<SuspectData> navigationList = GetProfileNavigationList();
        return navigationList.Count > 0
               && _currentProfileIndex >= 0
               && _currentProfileIndex < navigationList.Count - 1;
    }

    public bool CanOpenPreviousProfile()
    {
        return GetProfileNavigationList().Count > 0
               && _currentProfileIndex > 0;
    }

    private void ClearCurrentProfileSelection()
    {
        _currentProfileSuspect = null;
        _currentProfileIndex = -1;
    }

    private int GetProfileNavigationIndex(SuspectData suspectData)
    {
        List<SuspectData> navigationList = GetProfileNavigationList();
        if (suspectData == null || navigationList.Count == 0)
            return -1;

        for (int i = 0; i < navigationList.Count; i++)
        {
            SuspectData suspect = navigationList[i];

            if (suspect == null)
                continue;

            // Best case: same object reference
            if (suspect == suspectData)
                return i;

            // Fallback: identify by core fields
            if (AreSameSuspect(suspect, suspectData))
                return i;
        }

        return -1;
    }

    private List<SuspectData> GetProfileNavigationList()
    {
        if (_currentVisibleList != null)
            return _currentVisibleList;

        if (_currentBaseList != null)
            return _currentBaseList;

        return new List<SuspectData>();
    }
    private bool AreSameSuspect(SuspectData a, SuspectData b)
    {
        if (a == null || b == null)
            return false;

        return a.FirstName == b.FirstName
               && a.LastName == b.LastName
               && a.DateOfBirth == b.DateOfBirth;
    }

    // --------------------------------------------------
    // CATEGORY / FOLDER BUTTONS
    // --------------------------------------------------

    public void OpenRegistry()
    {
        PrepareForwardNavigation();
        _mainMenuMode = MainMenuMode.Registry;
        ShowPCBackButton();
        _currentNavigationState = new TerminalNavigationState
        {
            Screen = TerminalScreen.RegistryMenu,
            MenuMode = MainMenuMode.Registry,
            Section = _currentSection
        };
        OpenScreen(mainScreen);
    }

    public void OpenResidents()
    {
        PrepareForwardNavigation();
        _currentSection = TerminalSection.Residents;
        _showQuarantineDays = false;
        _currentBaseList = SortSuspects(_suspectSet.suspects
            .Where(s => s != null));

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();
        SetCurrentListNavigationState();

        OpenScreen(suspectListScreen);
        SetFolderTabsVisible(true);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenNews()
    {
        PrepareForwardNavigation();
        _currentSection = TerminalSection.News;
        _showQuarantineDays = false;
        _currentBaseList = new List<SuspectData>();
        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        _currentNewsEntries = BuildNewsEntries();
        ClearCurrentProfileSelection();
        SetCurrentListNavigationState();

        OpenScreen(suspectListScreen);
        SetFolderTabsVisible(false);
        RenderCurrentList();
    }

    public void OpenNewsEntryPage(TerminalNewsEntry newsEntry)
    {
        OpenNewsEntryPage(newsEntry, true);
    }

    private void OpenNewsEntryPage(TerminalNewsEntry newsEntry, bool recordHistory)
    {
        if (newsEntry == null)
            return;

        if (recordHistory)
            PrepareForwardNavigation();

        _currentNewsEntry = newsEntry;
        ClearCurrentProfileSelection();
        CloseAllScreens();
        profilePage.gameObject.SetActive(true);
        profilePage.SetNewsData(newsEntry);
        RefreshProfileUpDirectoryButton();
        _currentNavigationState = new TerminalNavigationState
        {
            Screen = TerminalScreen.NewsEntry,
            MenuMode = _mainMenuMode,
            Section = TerminalSection.News,
            NewsEntry = newsEntry
        };

        StartCoroutine(WaitAndRefreshMouse());
    }

    public void OpenDeceased()
    {
        PrepareForwardNavigation();
        _currentSection = TerminalSection.Deceased;
        _showQuarantineDays = false;
        _currentBaseList = SortSuspects(_suspectSet.suspects
            .Where(s =>
            {
                if (s == null) return false;
                SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(s);
                return record != null && record.isKilled;
            }));

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();
        SetCurrentListNavigationState();

        OpenScreen(suspectListScreen);
        SetFolderTabsVisible(true);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenQuarantine()
    {
        PrepareForwardNavigation();
        _currentSection = TerminalSection.Quarantine;
        _showQuarantineDays = true;
        int currentDay = GetCurrentDay();

        IEnumerable<SuspectData> quarantinedSuspects = SuspectRunRecords.Instance != null
            ? SuspectRunRecords.Instance.GetActiveQuarantineRecords(currentDay)
                .Where(record => record?.SuspectData != null)
                .Select(record => record.SuspectData)
            : Enumerable.Empty<SuspectData>();

        _currentBaseList = SortSuspects(quarantinedSuspects);
        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();
        SetCurrentListNavigationState();

        OpenScreen(suspectListScreen);
        SetFolderTabsVisible(true);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenAll()
    {
        PrepareForwardNavigation();
        _currentSection = TerminalSection.All;
        _showQuarantineDays = false;
        _currentBaseList = SortSuspects(_suspectSet.suspects
            .Where(s => s != null));

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();
        SetCurrentListNavigationState();

        OpenScreen(suspectListScreen);
        SetFolderTabsVisible(true);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public string GetTerminalStatus(SuspectData suspectData)
    {
        string status = GetBaseStatus(suspectData);
        if (status != "QUARANTINED" || !_showQuarantineDays)
            return status;

        int daysLeft = SuspectRunRecords.Instance != null
            ? SuspectRunRecords.Instance.GetRemainingQuarantineDays(suspectData, GetCurrentDay())
            : 0;

        string dayLabel = daysLeft == 1 ? "DAY" : "DAYS";
        return $"QUARANTINED - {daysLeft} {dayLabel} LEFT";
    }

    private string GetProfileEntryReason(SuspectData suspectData)
    {
        if (suspectData == null)
            return string.Empty;

        if (!HasMetSuspect(suspectData))
            return "unknown";

        SuspectPaperworkModel paperworkModel = new SuspectPaperworkModel();
        SuspectPaperworkService paperworkService = new SuspectPaperworkService(paperworkModel);
        SuspectPaperworkState state = paperworkService.BuildForPreview(
            suspectData,
            suspectData.IDPhoto,
            Array.Empty<string>(),
            GetPaperworkDay(),
            GetSuspectSetIndex(suspectData));

        paperworkModel.Dispose();

        return string.IsNullOrWhiteSpace(state.EntryReason) ? "unknown" : state.EntryReason;
    }

    private bool HasMetSuspect(SuspectData suspectData)
    {
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        return record != null
               && (record.daysShown > 0
                   || record.lastDayShown > 0
                   || record.hasEnteredCity
                   || record.isKilled
                   || record.quarantinedOnDay >= 0);
    }
    private string GetProfileLastEntryDate(SuspectData suspectData)
    {
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record == null || record.lastDayShown <= 0)
            return "unknown";

        return NewspaperStartDate.AddDays(record.lastDayShown - 1).ToString("MM/dd/yy");
    }

    private int GetPaperworkDay()
    {
        if (ShiftManager.Instance != null)
            return ShiftManager.Instance.CurrentDay;

        if (CampaignManager.Instance != null)
            return CampaignManager.Instance.CurrentDay;

        return 1;
    }

    private int GetSuspectSetIndex(SuspectData suspectData)
    {
        if (suspectData == null || _suspectSet == null || _suspectSet.suspects == null)
            return 0;

        int index = _suspectSet.suspects.IndexOf(suspectData);
        return index >= 0 ? index : 0;
    }

    private string GetProfileStatus(SuspectData suspectData)
    {
        return ToProfileDisplayStatus(GetBaseStatus(suspectData));
    }

    private static string ToProfileDisplayStatus(string status)
    {
        return status switch
        {
            "DECEASED" => "Deceased",
            "QUARANTINED" => "Quarantined",
            "REPLACED" => "Replaced",
            _ => "Alive"
        };
    }

    private string GetBaseStatus(SuspectData suspectData)
    {
        if (suspectData == null)
            return "CLEAR";

        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record == null)
            return "CLEAR";

        if (record.isReplacement)
            return "REPLACED";

        if (record.isKilled)
            return "DECEASED";

        if (SuspectRunRecords.Instance != null
            && SuspectRunRecords.Instance.IsInActiveQuarantine(suspectData, GetCurrentDay()))
            return "QUARANTINED";

        return "CLEAR";
    }

    private int GetCurrentDay()
        => _debugCurrentDayOverride >= 0
            ? _debugCurrentDayOverride
            : CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;

    private List<TerminalNewsEntry> BuildNewsEntries()
    {
        var entries = new List<TerminalNewsEntry>();

        if (_newspaperContentScriptables == null || _newspaperContentScriptables.Length == 0)
            return entries;

        int currentDay = Mathf.Max(1, GetCurrentDay());
        int lastReadableDay = Mathf.Min(currentDay, _newspaperContentScriptables.Length);

        for (int day = lastReadableDay; day >= 1; day--)
        {
            NewspaperContentScriptable content = _newspaperContentScriptables[day - 1];
            if (content == null)
                continue;

            string date = NewspaperStartDate.AddDays(day - 1).ToString("dd MMM yyyy").ToUpperInvariant();
            entries.Add(new TerminalNewsEntry(day, date, content));
        }

        return entries;
    }

    private static List<SuspectData> SortSuspects(IEnumerable<SuspectData> suspects)
    {
        return suspects
            .Where(HasTerminalName)
            .OrderBy(s => GetSortableLastName(s.LastName))
            .ThenBy(s => s.FirstName)
            .ToList();
    }

    private static bool HasTerminalName(SuspectData suspect)
    {
        return suspect != null
               && !string.IsNullOrWhiteSpace(suspect.LastName)
               && !string.IsNullOrWhiteSpace(suspect.FirstName);
    }

    private static string GetSortableLastName(string lastName)
    {
        return NormalizeForAlphabet(lastName);
    }

    private static char GetAlphabetFilterChar(string lastName)
    {
        string normalized = NormalizeForAlphabet(lastName);
        return string.IsNullOrEmpty(normalized) ? '\0' : normalized[0];
    }

    private static string NormalizeForAlphabet(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string normalized = value.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(normalized.Length);

        for (int i = 0; i < normalized.Length; i++)
        {
            char character = normalized[i];
            UnicodeCategory category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(character));
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    // --------------------------------------------------
    // LETTER CHUNK BUTTONS
    // --------------------------------------------------

    public void FilterAF()
    {
        FilterByLastNameRange('A', 'F');
        PCScrollbar.ResetToTop();
        SelectFolderTab(0);
    }

    public void FilterGL()
    {
        FilterByLastNameRange('G', 'L');
        PCScrollbar.ResetToTop();
        SelectFolderTab(1);
    }

    public void FilterMR()
    {
        FilterByLastNameRange('M', 'R');
        PCScrollbar.ResetToTop();
        SelectFolderTab(2);
    }

    public void FilterSZ()
    {
        FilterByLastNameRange('S', 'Z');
        PCScrollbar.ResetToTop();
        SelectFolderTab(3);
    }

    private void SetFolderTabsVisible(bool visible)
    {
        if (_folderTabs == null)
            return;

        for (int i = 0; i < _folderTabs.Length; i++)
        {
            if (_folderTabs[i] != null)
                _folderTabs[i].gameObject.SetActive(visible);
        }
    }

    void SelectFolderTab(int folderTabIndex)
    {
        if (_folderTabs == null)
            return;

        for (int i = 0; i < _folderTabs.Length; i++)
        {
            if (_folderTabs[i] != null)
                _folderTabs[i].SetFolderTabSelected(i == folderTabIndex);
        }
    }

    public void ClearLetterFilter()
    {
        if (_currentSection == TerminalSection.News)
        {
            RenderCurrentList();
            return;
        }

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        RenderCurrentList();
    }

    private void FilterByLastNameRange(char start, char end)
    {
        if (_currentSection == TerminalSection.News)
        {
            RenderCurrentList();
            return;
        }

        if (_currentBaseList == null || _currentBaseList.Count == 0)
        {
            _currentVisibleList = new List<SuspectData>();
            RenderCurrentList();
            return;
        }

        _currentVisibleList = _currentBaseList
            .Where(s =>
            {
                if (s == null || string.IsNullOrWhiteSpace(s.LastName))
                    return false;

                char firstChar = GetAlphabetFilterChar(s.LastName);

                return firstChar >= start && firstChar <= end;
            })
            .OrderBy(s => GetSortableLastName(s.LastName))
            .ThenBy(s => s.FirstName)
            .ToList();

        RenderCurrentList();
    }

    // --------------------------------------------------
    // UI RENDER
    // --------------------------------------------------

    private void RenderCurrentList()
    {
        if (_currentSection == TerminalSection.News)
        {
            terminalRecordListUI.ShowNews(_currentNewsEntries, GetSectionSummary());
            RefreshMouseNow();
            StartCoroutine(WaitAndRefreshMouse());
            return;
        }

        terminalRecordListUI.ShowRecords(_currentVisibleList, GetSectionSummary());
        RefreshMouseNow();
        StartCoroutine(WaitAndRefreshMouse());
    }

    private string GetSectionSummary()
    {
        switch (_currentSection)
        {
            case TerminalSection.Quarantine:
                int currentDay = GetCurrentDay();
                int activeQuarantine = SuspectRunRecords.Instance != null
                    ? SuspectRunRecords.Instance.GetActiveQuarantineCount(currentDay)
                    : 0;
                int remainingSlots = Mathf.Max(0, SuspectRunRecords.QuarantineSlotLimit - activeQuarantine);
                return $"QUARANTINE: {activeQuarantine}/{SuspectRunRecords.QuarantineSlotLimit} USED, {remainingSlots} OPEN";

            case TerminalSection.Deceased:
                return $"DECEASED: {GetDeceasedCount()}";

            case TerminalSection.Residents:
                return $"RESIDENTS: {_currentBaseList?.Count ?? 0}";

            case TerminalSection.News:
                int newsCount = _currentNewsEntries?.Count ?? 0;
                return newsCount > 0
                    ? $"NEWS ARCHIVE: {newsCount} ISSUES"
                    : "NEWS ARCHIVE: NO ISSUES";

            default:
                return $"RECORDS: {_currentBaseList?.Count ?? 0}";
        }
    }

    private int GetDeceasedCount()
    {
        if (_suspectSet == null || _suspectSet.suspects == null)
            return 0;

        return _suspectSet.suspects.Count(suspect =>
            HasTerminalName(suspect)
            && SuspectRunRecords.Instance?.GetRecord(suspect)?.isKilled == true);
    }

    private void RefreshMainMenu()
    {
        if (mainScreen == null)
            return;

        TextMeshProUGUI[] labels = mainScreen.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI label in labels)
        {
            if (label == null)
                continue;

            string labelText = label.text?.Trim();
            if (string.IsNullOrEmpty(labelText))
                continue;

            if (IsMainMenuTitle(label, labelText))
            {
                label.text = _mainMenuMode == MainMenuMode.Registry ? "Registry" : MainMenuVillageName;
                label.rectTransform.anchoredPosition = MainMenuTitlePosition;
                label.enableAutoSizing = true;
                label.fontSizeMin = Mathf.Min(label.fontSizeMin, 14f);
                label.fontSizeMax = Mathf.Max(label.fontSizeMax, label.fontSize);
                EnsureMainMenuPopulationLabel(label);
                continue;
            }

            RefreshMainMenuItem(label, labelText);
        }

        if (_mainMenuPopulationLabel != null)
            _mainMenuPopulationLabel.gameObject.SetActive(_mainMenuMode == MainMenuMode.Root);
    }
    private bool IsMainMenuTitle(TextMeshProUGUI label, string labelText)
    {
        if (label.transform.parent != mainScreen.transform)
            return false;

        return labelText == "Archive Index"
               || labelText == "Saplavi"
               || labelText == "Registry"
               || labelText.StartsWith(MainMenuVillageName, StringComparison.Ordinal);
    }

    private void RefreshMainMenuItem(TextMeshProUGUI label, string labelText)
    {
        if (_mainMenuMode == MainMenuMode.Registry)
        {
            RefreshRegistryMenuItem(label, labelText);
            return;
        }

        RefreshRootMenuItem(label, labelText);
    }

    private void RefreshRootMenuItem(TextMeshProUGUI label, string labelText)
    {
        if (labelText == "Residents" || labelText == "Registry")
        {
            ConfigureMainMenuButton(label, "Registry", true, OpenRegistry);
            return;
        }

        if (labelText == "Visitors" || labelText == "News")
        {
            ConfigureMainMenuButton(label, "News", true, OpenNews);
            return;
        }

        if (labelText == "Show All" || labelText == "All" || labelText == "Deceased" || labelText == "Quarantine")
            ConfigureMainMenuButton(label, labelText, false, null);
    }

    private void RefreshRegistryMenuItem(TextMeshProUGUI label, string labelText)
    {
        if (labelText == "Show All" || labelText == "All")
        {
            ConfigureMainMenuButton(label, "All", true, OpenAll);
            return;
        }

        if (labelText == "Residents" || labelText == "Registry")
        {
            ConfigureMainMenuButton(label, "Residents", true, OpenResidents);
            return;
        }

        if (labelText == "Visitors" || labelText == "News")
        {
            ConfigureMainMenuButton(label, labelText, false, null);
            return;
        }

        if (labelText == "Quarantine")
        {
            ConfigureMainMenuButton(label, "Quarantine", true, OpenQuarantine);
            return;
        }

        if (labelText == "Deceased")
            ConfigureMainMenuButton(label, "Deceased", true, OpenDeceased);
    }

    private static void ConfigureMainMenuButton(TextMeshProUGUI label, string text, bool active, UnityEngine.Events.UnityAction onClick)
    {
        label.text = text;

        ClickablePCElement button = label.GetComponentInParent<ClickablePCElement>(true);
        if (button == null)
            return;

        button.gameObject.SetActive(active);
        button.onClickEvent = new UnityEngine.Events.UnityEvent();

        if (onClick != null)
            button.onClickEvent.AddListener(onClick);
    }

    private int GetPopulationAliveForTerminal()
    {
        return _populationModel != null
            ? _populationModel.PopulationAlive.CurrentValue
            : DefaultTerminalPopulation;
    }

    private void EnsureMainMenuPopulationLabel(TextMeshProUGUI titleLabel)
    {
        if (titleLabel == null)
            return;

        if (_mainMenuPopulationLabel == null)
        {
            Transform existing = titleLabel.transform.parent != null
                ? titleLabel.transform.parent.Find(MainMenuPopulationObjectName)
                : null;

            if (existing != null)
            {
                _mainMenuPopulationLabel = existing.GetComponent<TextMeshProUGUI>();
            }
            else
            {
                GameObject labelObject = new GameObject(MainMenuPopulationObjectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(TextMeshProUGUI));
                labelObject.transform.SetParent(titleLabel.transform.parent, false);
                _mainMenuPopulationLabel = labelObject.GetComponent<TextMeshProUGUI>();
            }
        }

        RectTransform titleRect = titleLabel.rectTransform;
        RectTransform populationRect = _mainMenuPopulationLabel.rectTransform;
        populationRect.anchorMin = new Vector2(0.5f, 0.5f);
        populationRect.anchorMax = new Vector2(0.5f, 0.5f);
        populationRect.pivot = new Vector2(0.5f, 0.5f);
        populationRect.localRotation = titleRect.localRotation;
        populationRect.localScale = titleRect.localScale;
        populationRect.anchoredPosition = MainMenuPopulationPosition;
        populationRect.sizeDelta = new Vector2(titleRect.sizeDelta.x, titleRect.sizeDelta.y);

        _mainMenuPopulationLabel.text = $"Population: {GetPopulationAliveForTerminal()}";
        _mainMenuPopulationLabel.font = titleLabel.font;
        _mainMenuPopulationLabel.fontSharedMaterial = titleLabel.fontSharedMaterial;
        _mainMenuPopulationLabel.color = titleLabel.color;
        _mainMenuPopulationLabel.alignment = TextAlignmentOptions.Center;
        _mainMenuPopulationLabel.enableAutoSizing = true;
        _mainMenuPopulationLabel.fontSizeMin = Mathf.Min(titleLabel.fontSizeMin, 12f);
        _mainMenuPopulationLabel.fontSizeMax = Mathf.Max(titleLabel.fontSize, 22f);
        _mainMenuPopulationLabel.raycastTarget = false;
        _mainMenuPopulationLabel.gameObject.SetActive(true);
    }

    private static void SetMainMenuItemActive(TextMeshProUGUI label, bool active)
    {
        ClickablePCElement button = label.GetComponentInParent<ClickablePCElement>(true);
        if (button != null)
            button.gameObject.SetActive(active);
    }
}
