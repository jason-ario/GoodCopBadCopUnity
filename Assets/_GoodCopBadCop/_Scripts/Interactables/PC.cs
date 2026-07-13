using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using GoodCopBadCop.SuspectPaperwork;
using TMPro;
using UnityEngine;

public class PC : Interactable
{
    private enum TerminalSection
    {
        All,
        Residents,
        Visitors,
        Deceased,
        Quarantine,
        News
    }

    private enum MainMenuMode
    {
        Root,
        Registry
    }

    private const int MainMenuPopulation = 300;
    private const string MainMenuVillageName = "Saplavi";
    private const string MainMenuPopulationObjectName = "Main Menu Population";
    private static readonly Vector2 MainMenuTitlePosition = new Vector2(-0.056f, 1.168f);
    private static readonly Vector2 MainMenuPopulationPosition = new Vector2(0f, -0.85f);

    [Header("Data")]
    [SerializeField] private SuspectSet _suspectSet;

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

    // Profile navigation state
    private SuspectData _currentProfileSuspect;
    private int _currentProfileIndex = -1;
    [SerializeField] private PCFolderTab[] _folderTabs;
    private bool isOn;
    
    // List management
    private List<SuspectData> _currentBaseList;
    private List<SuspectData> _currentVisibleList;
    private bool _showQuarantineDays;
    private TerminalSection _currentSection = TerminalSection.All;
    private int _debugCurrentDayOverride = -1;
    private TextMeshProUGUI _mainMenuPopulationLabel;
    private MainMenuMode _mainMenuMode = MainMenuMode.Root;

    private void Start()
    {
        CloseAllScreens();
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        player.playerMovementController.SetCanControl(false);
        player.SetCanInteract(false, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        UIController.Instance.ShowBackButton(ExitPC);

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
            OpenScreen(mainScreen);
        }

        ClearCurrentProfileSelection();
    }

    private void Update()
    {
        if (!pcActive) return;

        if (Input.GetButtonDown("Back"))
        {
            ExitPC();
        }
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

        if (_virtualCanvasCursor != null)
            _virtualCanvasCursor.enabled = true;

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;

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
        if (suspectData == null)
            return;

        _currentProfileSuspect = suspectData;
        _currentProfileIndex = GetBaseListIndex(suspectData);

        CloseAllScreens();
        profilePage.gameObject.SetActive(true);

        profilePage.SetProfileData(
            suspectData,
            GetProfileEntryReason(suspectData),
            GetProfileLastExitDate(),
            GetProfileStatus(suspectData));

        UpdateProfileNavigationUI();

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
            _currentProfileIndex = GetBaseListIndex(_currentProfileSuspect);

        if (_currentProfileIndex < 0)
            return;

        int nextIndex = _currentProfileIndex + 1;

        if (nextIndex >= _currentBaseList.Count)
            nextIndex = _currentBaseList.Count - 1;

        SuspectData nextSuspect = _currentBaseList[nextIndex];
        if (nextSuspect == null)
            return;

        OpenProfilePage(nextSuspect); // already updates UI
    }
    
    public void OpenPreviousProfile()
    {
        if (_currentBaseList == null || _currentBaseList.Count == 0)
            return;

        if (_currentProfileSuspect == null)
            return;

        if (_currentProfileIndex < 0)
            _currentProfileIndex = GetBaseListIndex(_currentProfileSuspect);

        if (_currentProfileIndex < 0)
            return;

        int previousIndex = _currentProfileIndex - 1;

        // Clamp to first entry
        if (previousIndex < 0)
            previousIndex = 0;

        SuspectData previousSuspect = _currentBaseList[previousIndex];
        if (previousSuspect == null)
            return;

        OpenProfilePage(previousSuspect);
    }

    public bool CanOpenNextProfile()
    {
        return _currentBaseList != null
               && _currentBaseList.Count > 0
               && _currentProfileIndex >= 0
               && _currentProfileIndex < _currentBaseList.Count - 1;
    }

    public bool CanOpenPreviousProfile()
    {
        return _currentBaseList != null
               && _currentBaseList.Count > 0
               && _currentProfileIndex > 0;
    }

    private void ClearCurrentProfileSelection()
    {
        _currentProfileSuspect = null;
        _currentProfileIndex = -1;
    }

    private int GetBaseListIndex(SuspectData suspectData)
    {
        if (suspectData == null || _currentBaseList == null || _currentBaseList.Count == 0)
            return -1;

        for (int i = 0; i < _currentBaseList.Count; i++)
        {
            SuspectData suspect = _currentBaseList[i];

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
        _mainMenuMode = MainMenuMode.Registry;
        OpenScreen(mainScreen);
    }

    public void OpenResidents()
    {
        _currentSection = TerminalSection.Residents;
        _showQuarantineDays = false;
        _currentBaseList = SortSuspects(_suspectSet.suspects
            .Where(s => s != null && s.IsResident));

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenVisitors()
    {
        _currentSection = TerminalSection.Visitors;
        _showQuarantineDays = false;
        _currentBaseList = SortSuspects(_suspectSet.suspects
            .Where(s => s != null && !s.IsResident));

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenNews()
    {
        _currentSection = TerminalSection.News;
        _showQuarantineDays = false;
        _currentBaseList = new List<SuspectData>();
        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        OpenScreen(suspectListScreen);
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenDeceased()
    {
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

        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenQuarantine()
    {
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

        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenAll()
    {
        _currentSection = TerminalSection.All;
        _showQuarantineDays = false;
        _currentBaseList = SortSuspects(_suspectSet.suspects
            .Where(s => s != null));

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        OpenScreen(suspectListScreen);
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

    private string GetProfileLastExitDate()
    {
        if (ShiftManager.Instance == null)
            return "unknown";

        return ShiftManager.Instance.CurrentGameDate.ToString("MM/dd/yy");
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
        string status = GetBaseStatus(suspectData);
        return status == "CLEAR" ? string.Empty : status;
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

    private static List<SuspectData> SortSuspects(IEnumerable<SuspectData> suspects)
    {
        return suspects
            .Where(HasTerminalName)
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();
    }

    private static bool HasTerminalName(SuspectData suspect)
    {
        return suspect != null
               && !string.IsNullOrWhiteSpace(suspect.LastName)
               && !string.IsNullOrWhiteSpace(suspect.FirstName);
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

    void SelectFolderTab(int folderTabIndex)
    {
        for (int i = 0; i < _folderTabs.Length; i++)
        {
            _folderTabs[i].SetFolderTabSelected(i == folderTabIndex);
        }
    }

    public void ClearLetterFilter()
    {
        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        RenderCurrentList();
    }

    private void FilterByLastNameRange(char start, char end)
    {
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

                string trimmedLastName = s.LastName.Trim();
                char firstChar = char.ToUpper(trimmedLastName[0]);

                return firstChar >= start && firstChar <= end;
            })
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();

        RenderCurrentList();
    }

    // --------------------------------------------------
    // UI RENDER
    // --------------------------------------------------

    private void RenderCurrentList()
    {
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

            case TerminalSection.Visitors:
                return $"VISITORS: {_currentBaseList?.Count ?? 0}";

            case TerminalSection.News:
                return "NEWS";

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
            ConfigureMainMenuButton(label, "Visitors", true, OpenVisitors);
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

        _mainMenuPopulationLabel.text = $"Population: {MainMenuPopulation}";
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
