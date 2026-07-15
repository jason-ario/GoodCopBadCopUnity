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
    private enum TerminalSection { All, Residents, Deceased, Quarantine, News }
    private enum TerminalScreen { RootMenu, RegistryMenu, List, Profile, NewsEntry }

    private struct NavState
    {
        public TerminalScreen Screen;
        public TerminalSection Section;
        public SuspectData Suspect;
        public TerminalNewsEntry News;
        public static NavState Root() => new NavState { Screen = TerminalScreen.RootMenu, Section = TerminalSection.All };
    }

    private const string VillageName = "Saplavi";
    private const int DefaultTerminalPopulation = 300;
    private static readonly DateTime NewspaperStartDate = new DateTime(1989, 10, 20);

    [Header("Data")]
    [SerializeField] private SuspectSet _suspectSet;
    [SerializeField] private NewspaperContentScriptable[] _newspaperContentScriptables;

    [Header("Set Up")]
    [SerializeField] private GameObject computerCamera;
    [SerializeField] private Transform lookAtTarget;
    [SerializeField] private Transform standPos;
    [SerializeField] private SimpleCanvasCursorFromMouseDelta _virtualCanvasCursor;

    [Header("Terminal")]
    [SerializeField] private TextMeshProUGUI header;
    [SerializeField] private FileListView fileListView;
    [SerializeField] private ProfileView profileView;
    [SerializeField] private NewsView newsView;
    [SerializeField] private ClickablePCElement backButton;
    [SerializeField] private ClickablePCElement previousButton;
    [SerializeField] private ClickablePCElement nextButton;
    [SerializeField] private SimpleCanvasCursorFromMouseDelta mouseCursor;



    [VContainer.Inject] private IPopulationModel _populationModel;

    private bool pcActive;
    private bool isOn;
    private PlayerInteractionController _player;
    private SuspectData _currentProfileSuspect;
    private int _currentProfileIndex = -1;
    private int _currentNewsIndex = -1;
    private List<SuspectData> _currentBaseList = new();
    private List<SuspectData> _currentVisibleList = new();
    private List<TerminalNewsEntry> _currentNewsEntries = new();
    private bool _showQuarantineDays;
    private TerminalSection _currentSection = TerminalSection.All;
    private int _debugCurrentDayOverride = -1;
    private readonly Stack<NavState> _backStack = new();
    private NavState _currentState = NavState.Root();
    private bool _restoring;

    private void Start()
    {
        CloseAllScreens();
        RefreshNavigationButtons();
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        player.playerMovementController.SetCanControl(false);
        player.SetCanInteract(false, "");
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        ShowPCBackButton();

        if (lookAtTarget != null)
            player.playerMovementController.LookAtTarget(lookAtTarget.transform);
        if (standPos != null)
        {
            player.transform.DOMove(standPos.position, 0.5f);
            player.transform.DORotate(standPos.rotation.eulerAngles, 0.5f);
        }
        if (computerCamera != null)
            player.playerMovementController.MoveCameraTo(computerCamera.transform);

        pcActive = true;
        _player = player;
        if (_virtualCanvasCursor != null)
            _virtualCanvasCursor.enabled = true;

        if (!isOn)
        {
            isOn = true;
            ResetNavigation();
            OpenRootMenu();
        }

        ClearCurrentProfileSelection();
    }

    private void Update()
    {
        if (pcActive && Input.GetButtonDown("Back"))
            HandleBackButton();
    }

    public void DebugSetCurrentDay(int currentDay) => _debugCurrentDayOverride = currentDay;

    public void DebugOpenTerminal()
    {
        pcActive = true;
        isOn = true;
        ResetNavigation();
        if (_virtualCanvasCursor != null)
            _virtualCanvasCursor.enabled = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        ShowPCBackButton();
        OpenRootMenu();
        ClearCurrentProfileSelection();
    }

    public void OpenScreen(GameObject screen)
    {
        OpenRootMenu();
    }

    public void CloseAllScreens()
    {
        SetViewActive(fileListView, false);
        SetViewActive(profileView, false);
        SetViewActive(newsView, false);
    }
    public void OpenRegistry()
    {
        PushCurrentState();
        _currentState = new NavState { Screen = TerminalScreen.RegistryMenu, Section = _currentSection };
        ShowFileList("Registry", "Folders", new List<PCListItemModel>
        {
            new PCListItemModel("All", PCListItemIcon.Folder, OpenAll),
            new PCListItemModel("Residents", PCListItemIcon.Folder, OpenResidents),
            new PCListItemModel("Quarantine", PCListItemIcon.Folder, OpenQuarantine),
            new PCListItemModel("Deceased", PCListItemIcon.Folder, OpenDeceased)
        });
    }

    public void OpenResidents() => OpenSuspectList(TerminalSection.Residents, $"RESIDENTS: {GetAllNamedSuspects().Count}", GetAllNamedSuspects(), false);
    public void OpenAll() => OpenSuspectList(TerminalSection.All, $"RECORDS: {GetAllNamedSuspects().Count}", GetAllNamedSuspects(), false);
    public void OpenDeceased() => OpenSuspectList(TerminalSection.Deceased, $"DECEASED: {GetDeceasedCount()}", GetAllNamedSuspects().Where(IsDeceased), false);

    public void OpenQuarantine()
    {
        int day = GetCurrentDay();
        int active = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.GetActiveQuarantineCount(day) : 0;
        int open = Mathf.Max(0, SuspectRunRecords.QuarantineSlotLimit - active);
        IEnumerable<SuspectData> suspects = SuspectRunRecords.Instance != null
            ? SuspectRunRecords.Instance.GetActiveQuarantineRecords(day).Where(r => r?.SuspectData != null).Select(r => r.SuspectData)
            : Enumerable.Empty<SuspectData>();
        OpenSuspectList(TerminalSection.Quarantine, $"QUARANTINE: {active}/{SuspectRunRecords.QuarantineSlotLimit} USED, {open} OPEN", suspects, true);
    }

    public void OpenNews()
    {
        PushCurrentState();
        _currentSection = TerminalSection.News;
        _showQuarantineDays = false;
        _currentBaseList = new List<SuspectData>();
        _currentVisibleList = new List<SuspectData>();
        _currentNewsEntries = BuildNewsEntries();
        _currentNewsIndex = -1;
        ClearCurrentProfileSelection();
        _currentState = new NavState { Screen = TerminalScreen.List, Section = TerminalSection.News };
        ShowFileList("News", GetSectionSummary(), BuildNewsItems());
    }

    public void OpenProfilePage(SuspectData suspectData) => OpenProfilePage(suspectData, true);
    public void OpenNewsEntryPage(TerminalNewsEntry newsEntry) => OpenNewsEntryPage(newsEntry, true);

    public void OpenNextProfile()
    {
        List<SuspectData> list = GetProfileNavigationList();
        if (list.Count == 0 || _currentProfileIndex < 0) return;
        OpenProfilePage(list[Mathf.Min(_currentProfileIndex + 1, list.Count - 1)], false);
    }

    public void OpenPreviousProfile()
    {
        List<SuspectData> list = GetProfileNavigationList();
        if (list.Count == 0 || _currentProfileIndex < 0) return;
        OpenProfilePage(list[Mathf.Max(0, _currentProfileIndex - 1)], false);
    }

    public bool CanOpenNextProfile() => GetProfileNavigationList().Count > 0 && _currentProfileIndex >= 0 && _currentProfileIndex < GetProfileNavigationList().Count - 1;
    public bool CanOpenPreviousProfile() => GetProfileNavigationList().Count > 0 && _currentProfileIndex > 0;

    public string GetTerminalStatus(SuspectData suspectData)
    {
        string status = GetBaseStatus(suspectData);
        if (status != "QUARANTINED" || !_showQuarantineDays)
            return status;
        int daysLeft = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.GetRemainingQuarantineDays(suspectData, GetCurrentDay()) : 0;
        return $"QUARANTINED - {daysLeft} {(daysLeft == 1 ? "DAY" : "DAYS")} LEFT";
    }

    public void FilterAF() => FilterByLastNameRange('A', 'F');
    public void FilterGL() => FilterByLastNameRange('G', 'L');
    public void FilterMR() => FilterByLastNameRange('M', 'R');
    public void FilterSZ() => FilterByLastNameRange('S', 'Z');

    public void ClearLetterFilter()
    {
        if (_currentSection == TerminalSection.News)
        {
            ShowFileList("News", GetSectionSummary(), BuildNewsItems());
            return;
        }
        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        RenderCurrentList();
    }

    private void OpenRootMenu()
    {
        _currentState = NavState.Root();
        ShowFileList(VillageName, $"Population: {GetPopulationAliveForTerminal()}", new List<PCListItemModel>
        {
            new PCListItemModel("Registry", PCListItemIcon.Folder, OpenRegistry),
            new PCListItemModel("News", PCListItemIcon.Folder, OpenNews)
        });
    }

    private void OpenSuspectList(TerminalSection section, string label, IEnumerable<SuspectData> suspects, bool showQuarantineDays)
    {
        PushCurrentState();
        _currentSection = section;
        _showQuarantineDays = showQuarantineDays;
        _currentBaseList = SortSuspects(suspects ?? Enumerable.Empty<SuspectData>());
        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();
        _currentState = new NavState { Screen = TerminalScreen.List, Section = section };
        ShowFileList("Registry", label, BuildSuspectItems(_currentVisibleList));
    }

    private void OpenProfilePage(SuspectData suspectData, bool recordHistory)
    {
        if (suspectData == null) return;
        if (recordHistory) PushCurrentState();
        _currentProfileSuspect = suspectData;
        _currentProfileIndex = GetProfileNavigationIndex(suspectData);
        _currentNewsIndex = -1;
        CloseAllScreens();
        SetHeader("Profile");
        SetViewActive(profileView, true);
        profileView.Show(suspectData, GetProfileEntryReason(suspectData), GetProfileLastEntryDate(suspectData), GetProfileStatus(suspectData));
        _currentState = new NavState { Screen = TerminalScreen.Profile, Section = _currentSection, Suspect = suspectData };
        RefreshNavigationButtons();
        RefreshMouseDelayed();
    }

    private void OpenNewsEntryPage(TerminalNewsEntry newsEntry, bool recordHistory)
    {
        if (newsEntry == null) return;
        if (recordHistory) PushCurrentState();
        _currentNewsIndex = GetNewsNavigationIndex(newsEntry);
        ClearCurrentProfileSelection();
        CloseAllScreens();
        SetHeader("News");
        SetViewActive(newsView, true);
        newsView.Show(newsEntry);
        _currentState = new NavState { Screen = TerminalScreen.NewsEntry, Section = TerminalSection.News, News = newsEntry };
        RefreshNavigationButtons();
        RefreshMouseDelayed();
    }
    private void OpenNextNews()
    {
        if (_currentNewsEntries == null || _currentNewsEntries.Count == 0 || _currentNewsIndex < 0) return;
        OpenNewsEntryPage(_currentNewsEntries[Mathf.Min(_currentNewsIndex + 1, _currentNewsEntries.Count - 1)], false);
    }

    private void OpenPreviousNews()
    {
        if (_currentNewsEntries == null || _currentNewsEntries.Count == 0 || _currentNewsIndex < 0) return;
        OpenNewsEntryPage(_currentNewsEntries[Mathf.Max(0, _currentNewsIndex - 1)], false);
    }

    private bool CanOpenNextNews() => _currentNewsEntries != null && _currentNewsIndex >= 0 && _currentNewsIndex < _currentNewsEntries.Count - 1;
    private bool CanOpenPreviousNews() => _currentNewsEntries != null && _currentNewsIndex > 0;

    private int GetNewsNavigationIndex(TerminalNewsEntry newsEntry)
    {
        if (newsEntry == null || _currentNewsEntries == null) return -1;
        for (int i = 0; i < _currentNewsEntries.Count; i++)
            if (_currentNewsEntries[i] == newsEntry || _currentNewsEntries[i]?.Day == newsEntry.Day)
                return i;
        return -1;
    }

    private void RenderCurrentList()
    {
        if (_currentSection == TerminalSection.News)
        {
            ShowFileList("News", GetSectionSummary(), BuildNewsItems());
            return;
        }
        ShowFileList("Registry", GetSectionSummary(), BuildSuspectItems(_currentVisibleList));
    }

    private void ShowFileList(string headerText, string label, IReadOnlyList<PCListItemModel> items)
    {
        CloseAllScreens();
        SetHeader(headerText);
        SetViewActive(fileListView, true);
        fileListView.Show(label, items);
        RefreshNavigationButtons();
        RefreshMouseDelayed();
    }

    private List<PCListItemModel> BuildSuspectItems(IReadOnlyList<SuspectData> suspects)
    {
        var items = new List<PCListItemModel>();
        if (suspects == null) return items;
        for (int i = 0; i < suspects.Count; i++)
        {
            SuspectData suspect = suspects[i];
            if (suspect == null) continue;
            string displayName = suspect.LastName + ", " + suspect.FirstName;
            string status = GetTerminalStatus(suspect);
            string text = string.IsNullOrWhiteSpace(status) ? displayName : displayName + " - " + status;
            items.Add(new PCListItemModel(text, PCListItemIcon.Profile, () => OpenProfilePage(suspect), suspect.IDPhoto));
        }
        return items;
    }

    private List<PCListItemModel> BuildNewsItems()
    {
        var items = new List<PCListItemModel>();
        if (_currentNewsEntries == null) return items;
        for (int i = 0; i < _currentNewsEntries.Count; i++)
        {
            TerminalNewsEntry newsEntry = _currentNewsEntries[i];
            string newsHeader = newsEntry?.Content != null ? newsEntry.Content.headerText : "MISSING NEWS ENTRY";
            string date = newsEntry != null ? newsEntry.Date : "UNKNOWN DATE";
            items.Add(new PCListItemModel($"{date} - {newsHeader}", PCListItemIcon.Unknown, () => OpenNewsEntryPage(newsEntry), null, newsEntry?.Content != null));
        }
        return items;
    }

    private void RefreshNavigationButtons()
    {
        ConfigureButton(backButton, _backStack.Count > 0, () => GoBack());
        bool profileScreen = _currentState.Screen == TerminalScreen.Profile;
        bool newsScreen = _currentState.Screen == TerminalScreen.NewsEntry;
        ConfigureButton(previousButton, profileScreen ? CanOpenPreviousProfile() : newsScreen && CanOpenPreviousNews(), profileScreen ? OpenPreviousProfile : newsScreen ? OpenPreviousNews : null);
        ConfigureButton(nextButton, profileScreen ? CanOpenNextProfile() : newsScreen && CanOpenNextNews(), profileScreen ? OpenNextProfile : newsScreen ? OpenNextNews : null);
    }

    private static void ConfigureButton(ClickablePCElement button, bool visible, Action onClick)
    {
        if (button == null) return;
        button.gameObject.SetActive(visible);
        button.SetClickHandler(visible ? onClick : null);
    }

    private void PushCurrentState()
    {
        if (!_restoring)
            _backStack.Push(_currentState);
    }

    private bool GoBack()
    {
        if (_backStack.Count == 0) return false;
        NavState previous = _backStack.Pop();
        _restoring = true;
        ApplyNavigationState(previous);
        _restoring = false;
        return true;
    }

    private void ApplyNavigationState(NavState state)
    {
        switch (state.Screen)
        {
            case TerminalScreen.RegistryMenu: OpenRegistry(); break;
            case TerminalScreen.List: OpenSection(state.Section); break;
            case TerminalScreen.Profile: OpenProfilePage(state.Suspect, false); break;
            case TerminalScreen.NewsEntry: OpenNewsEntryPage(state.News, false); break;
            case TerminalScreen.RootMenu:
            default: OpenRootMenu(); break;
        }
    }

    private void OpenSection(TerminalSection section)
    {
        switch (section)
        {
            case TerminalSection.Residents: OpenResidents(); break;
            case TerminalSection.Deceased: OpenDeceased(); break;
            case TerminalSection.Quarantine: OpenQuarantine(); break;
            case TerminalSection.News: OpenNews(); break;
            case TerminalSection.All:
            default: OpenAll(); break;
        }
    }

    private void ResetNavigation()
    {
        _backStack.Clear();
        _currentState = NavState.Root();
        _currentNewsIndex = -1;
    }

    private void ClearCurrentProfileSelection()
    {
        _currentProfileSuspect = null;
        _currentProfileIndex = -1;
    }

    private int GetProfileNavigationIndex(SuspectData suspectData)
    {
        List<SuspectData> list = GetProfileNavigationList();
        if (suspectData == null || list.Count == 0) return -1;
        for (int i = 0; i < list.Count; i++)
            if (list[i] == suspectData || AreSameSuspect(list[i], suspectData))
                return i;
        return -1;
    }

    private List<SuspectData> GetProfileNavigationList()
    {
        if (_currentVisibleList != null) return _currentVisibleList;
        if (_currentBaseList != null) return _currentBaseList;
        return new List<SuspectData>();
    }

    private static bool AreSameSuspect(SuspectData a, SuspectData b)
    {
        return a != null && b != null && a.FirstName == b.FirstName && a.LastName == b.LastName && a.DateOfBirth == b.DateOfBirth;
    }
    private string GetProfileEntryReason(SuspectData suspectData)
    {
        if (suspectData == null) return string.Empty;
        if (!HasMetSuspect(suspectData)) return "unknown";
        SuspectPaperworkModel model = new SuspectPaperworkModel();
        SuspectPaperworkService service = new SuspectPaperworkService(model);
        SuspectPaperworkState state = service.BuildForPreview(suspectData, suspectData.IDPhoto, Array.Empty<string>(), GetPaperworkDay(), GetSuspectSetIndex(suspectData));
        model.Dispose();
        return string.IsNullOrWhiteSpace(state.EntryReason) ? "unknown" : state.EntryReason;
    }

    private bool HasMetSuspect(SuspectData suspectData)
    {
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        return record != null && (record.daysShown > 0 || record.lastDayShown > 0 || record.hasEnteredCity || record.isKilled || record.quarantinedOnDay >= 0);
    }

    private string GetProfileLastEntryDate(SuspectData suspectData)
    {
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record == null || record.lastDayShown <= 0) return "unknown";
        return NewspaperStartDate.AddDays(record.lastDayShown - 1).ToString("MM/dd/yy");
    }

    private int GetPaperworkDay()
    {
        if (ShiftManager.Instance != null) return ShiftManager.Instance.CurrentDay;
        if (CampaignManager.Instance != null) return CampaignManager.Instance.CurrentDay;
        return 1;
    }

    private int GetSuspectSetIndex(SuspectData suspectData)
    {
        if (suspectData == null || _suspectSet == null || _suspectSet.suspects == null) return 0;
        int index = _suspectSet.suspects.IndexOf(suspectData);
        return index >= 0 ? index : 0;
    }

    private string GetProfileStatus(SuspectData suspectData) => ToProfileDisplayStatus(GetBaseStatus(suspectData));

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
        if (suspectData == null) return "CLEAR";
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record == null) return "CLEAR";
        if (record.isReplacement) return "REPLACED";
        if (record.isKilled) return "DECEASED";
        if (SuspectRunRecords.Instance != null && SuspectRunRecords.Instance.IsInActiveQuarantine(suspectData, GetCurrentDay())) return "QUARANTINED";
        return "CLEAR";
    }

    private int GetCurrentDay()
    {
        if (_debugCurrentDayOverride >= 0) return _debugCurrentDayOverride;
        return CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : 1;
    }

    private List<TerminalNewsEntry> BuildNewsEntries()
    {
        var entries = new List<TerminalNewsEntry>();
        if (_newspaperContentScriptables == null || _newspaperContentScriptables.Length == 0) return entries;
        int lastDay = Mathf.Min(Mathf.Max(1, GetCurrentDay()), _newspaperContentScriptables.Length);
        for (int day = lastDay; day >= 1; day--)
        {
            NewspaperContentScriptable content = _newspaperContentScriptables[day - 1];
            if (content == null) continue;
            string date = NewspaperStartDate.AddDays(day - 1).ToString("dd MMM yyyy").ToUpperInvariant();
            entries.Add(new TerminalNewsEntry(day, date, content));
        }
        return entries;
    }

    private List<SuspectData> GetAllNamedSuspects()
    {
        if (_suspectSet == null || _suspectSet.suspects == null) return new List<SuspectData>();
        return SortSuspects(_suspectSet.suspects.Where(s => s != null));
    }

    private static List<SuspectData> SortSuspects(IEnumerable<SuspectData> suspects)
    {
        return suspects.Where(HasTerminalName).OrderBy(s => NormalizeForAlphabet(s.LastName)).ThenBy(s => s.FirstName).ToList();
    }

    private static bool HasTerminalName(SuspectData suspect)
    {
        return suspect != null && !string.IsNullOrWhiteSpace(suspect.LastName) && !string.IsNullOrWhiteSpace(suspect.FirstName);
    }

    private static char GetAlphabetFilterChar(string lastName)
    {
        string normalized = NormalizeForAlphabet(lastName);
        return string.IsNullOrEmpty(normalized) ? '\0' : normalized[0];
    }

    private static string NormalizeForAlphabet(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string normalized = value.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(normalized.Length);
        for (int i = 0; i < normalized.Length; i++)
        {
            char character = normalized[i];
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToUpperInvariant(character));
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    private void FilterByLastNameRange(char start, char end)
    {
        if (_currentSection == TerminalSection.News)
        {
            ShowFileList("News", GetSectionSummary(), BuildNewsItems());
            return;
        }
        _currentVisibleList = _currentBaseList.Where(s =>
        {
            if (s == null || string.IsNullOrWhiteSpace(s.LastName)) return false;
            char firstChar = GetAlphabetFilterChar(s.LastName);
            return firstChar >= start && firstChar <= end;
        }).OrderBy(s => NormalizeForAlphabet(s.LastName)).ThenBy(s => s.FirstName).ToList();
        RenderCurrentList();
    }

    private string GetSectionSummary()
    {
        switch (_currentSection)
        {
            case TerminalSection.Quarantine:
                int day = GetCurrentDay();
                int active = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.GetActiveQuarantineCount(day) : 0;
                int open = Mathf.Max(0, SuspectRunRecords.QuarantineSlotLimit - active);
                return $"QUARANTINE: {active}/{SuspectRunRecords.QuarantineSlotLimit} USED, {open} OPEN";
            case TerminalSection.Deceased:
                return $"DECEASED: {GetDeceasedCount()}";
            case TerminalSection.Residents:
                return $"RESIDENTS: {_currentBaseList?.Count ?? 0}";
            case TerminalSection.News:
                int count = _currentNewsEntries?.Count ?? 0;
                return count > 0 ? $"NEWS ARCHIVE: {count} ISSUES" : "NEWS ARCHIVE: NO ISSUES";
            default:
                return $"RECORDS: {_currentBaseList?.Count ?? 0}";
        }
    }

    private int GetDeceasedCount() => GetAllNamedSuspects().Count(IsDeceased);
    private bool IsDeceased(SuspectData suspect) => SuspectRunRecords.Instance?.GetRecord(suspect)?.isKilled == true;
    private int GetPopulationAliveForTerminal() => _populationModel != null ? _populationModel.PopulationAlive.CurrentValue : DefaultTerminalPopulation;
    private void SetHeader(string text)
    {
        if (header != null) header.text = text;
    }

    private static void SetViewActive(Component view, bool active)
    {
        if (view != null) view.gameObject.SetActive(active);
    }

    private void RefreshMouseNow()
    {
        if (mouseCursor != null) mouseCursor.SetScreenContent();
    }

    private void RefreshMouseDelayed()
    {
        RefreshMouseNow();
        if (gameObject.activeInHierarchy) StartCoroutine(WaitAndRefreshMouse());
    }

    private IEnumerator WaitAndRefreshMouse()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
        RefreshMouseNow();
    }

    private void ShowPCBackButton()
    {
        if (UIController.Instance == null) return;
        UIController.Instance.HideBackButton();
        UIController.Instance.ShowBackButton(HandleBackButton);
    }

    private void HandleBackButton()
    {
        if (GoBack()) return;
        ExitPC();
    }

    private void ExitPC()
    {
        pcActive = false;
        if (UIController.Instance != null) UIController.Instance.HideBackButton();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        if (_virtualCanvasCursor != null) _virtualCanvasCursor.enabled = false;
        if (_player == null) return;
        _player.SetCanInteract(true, "");
        _player.playerMovementController.ResetCameraPos(false, 0.5f, () => _player.playerMovementController.SetCanControl(true));
    }

}
