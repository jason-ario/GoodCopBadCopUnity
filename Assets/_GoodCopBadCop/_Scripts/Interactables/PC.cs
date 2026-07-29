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
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Terminal kiosk. Navigation is server-authoritative: whichever client is currently
/// operating the terminal sends navigation requests to the server, the server resolves
/// them into a compact <see cref="NavSyncState"/> and stores it in a replicated
/// <see cref="NetworkVariable{T}"/>. Every client (including the host) reacts to changes
/// of that NetworkVariable by deterministically rebuilding the exact same screen locally,
/// so the monitor's rendered Canvas/RenderTexture content and camera on/off state are
/// identical for every player, not just the one currently interacting.
/// </summary>
public class PC : Interactable
{
    private enum TerminalSection { All, Active, Deceased, Quarantine, News }
    private enum TerminalScreen { RootMenu, RegistryMenu, List, Profile, NewsEntry }
    private enum LetterFilter { None, AF, GL, MR, SZ }

    private enum NavAction
    {
        EnterTerminal,
        ExitTerminal,
        OpenRootMenu,
        OpenRegistry,
        OpenAll,
        OpenActive,
        OpenDeceased,
        OpenQuarantine,
        OpenNews,
        OpenProfile,
        OpenNewsEntry,
        NextProfile,
        PreviousProfile,
        NextNews,
        PreviousNews,
        FilterLetters,
        ClearFilter,
        Back
    }

    /// <summary>
    /// The full description of what the terminal screen should currently be showing.
    /// Deliberately minimal (only identifiers, never data references) so it can be
    /// replicated via a NetworkVariable: every client rebuilds the identical UI from
    /// these identifiers plus its own local (already-synced-elsewhere) game data.
    /// </summary>
    private struct NavSyncState : INetworkSerializable, IEquatable<NavSyncState>
    {
        public TerminalScreen Screen;
        public TerminalSection Section;
        public LetterFilter Filter;
        public int SuspectIndex;
        public int NewsDay;
        public bool IsActive;
        public bool HasBack;
        public bool HasPrevious;
        public bool HasNext;

        public static NavSyncState Root(bool active) => new NavSyncState
        {
            Screen = TerminalScreen.RootMenu,
            Section = TerminalSection.All,
            Filter = LetterFilter.None,
            SuspectIndex = -1,
            NewsDay = -1,
            IsActive = active,
            HasBack = false,
            HasPrevious = false,
            HasNext = false
        };

        public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
        {
            serializer.SerializeValue(ref Screen);
            serializer.SerializeValue(ref Section);
            serializer.SerializeValue(ref Filter);
            serializer.SerializeValue(ref SuspectIndex);
            serializer.SerializeValue(ref NewsDay);
            serializer.SerializeValue(ref IsActive);
            serializer.SerializeValue(ref HasBack);
            serializer.SerializeValue(ref HasPrevious);
            serializer.SerializeValue(ref HasNext);
        }

        public bool Equals(NavSyncState other) =>
            Screen == other.Screen && Section == other.Section && Filter == other.Filter &&
            SuspectIndex == other.SuspectIndex && NewsDay == other.NewsDay && IsActive == other.IsActive &&
            HasBack == other.HasBack && HasPrevious == other.HasPrevious && HasNext == other.HasNext;

        public override bool Equals(object obj) => obj is NavSyncState other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Screen, Section, Filter, SuspectIndex, NewsDay, IsActive);
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
    [Tooltip("The Camera that renders the terminal UI onto the monitor's render texture. Only needs to be active while a player is using the terminal.")]
    [SerializeField] private GameObject screenRenderCamera;
    [SerializeField] private AudioClip enterPCViewSfx;

    [Header("Idle Sound")]
    [Tooltip("Dedicated looping AudioSource used for the terminal's idle hum while the PC view is active. Should have loop enabled and playOnAwake disabled.")]
    [SerializeField] private AudioSource idleAudioSource;
    [SerializeField] private AudioClip idleSfx;
    [SerializeField, Range(0f, 1f)] private float idleSfxVolume = 1f;
    [SerializeField] private float idleFadeDuration = 0.5f;

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

    // ── Networked navigation state (server-authoritative) ───────────────────────
    private readonly NetworkVariable<NavSyncState> _navState = new NetworkVariable<NavSyncState>(
        NavSyncState.Root(false),
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    /// <summary>Debug "current day" override, replicated so every client's deterministic list rebuilding stays in sync.</summary>
    private readonly NetworkVariable<int> _debugDayOverrideNet = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    // ── Server-only bookkeeping (never networked directly; only its effects are) ─
    private readonly Stack<NavSyncState> _serverHistory = new();
    private bool _serverRestoring;

    // ── Local, per-player state ──────────────────────────────────────────────────
    private bool pcActive;
    private int _debugCurrentDayOverride = -1;
    private PlayerInteractionController _player;
    private Coroutine _idleFadeCoroutine;

    private void Start()
    {
        CloseAllScreens();
        SetScreenRenderCameraActive(false);
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _navState.OnValueChanged += HandleNavStateChanged;
        ApplySyncedState(_navState.Value);
    }

    public override void OnNetworkDespawn()
    {
        _navState.OnValueChanged -= HandleNavStateChanged;
        base.OnNetworkDespawn();
    }

    private void HandleNavStateChanged(NavSyncState previous, NavSyncState current) => ApplySyncedState(current);

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);
        SFXController.Instance?.Play(enterPCViewSfx);
        player.playerMovementController.SetCanControl(false);
        player.SetCanInteract(false, "");
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        ShowPCBackButton();
        UIController.Instance?.ClosePlayerUI();

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

        if (!_navState.Value.IsActive)
            Navigate(NavAction.EnterTerminal);
    }

    private void Update()
    {
        if (pcActive && Input.GetButtonDown("Back"))
            HandleBackButton();
    }

    public void DebugSetCurrentDay(int currentDay)
    {
        _debugCurrentDayOverride = currentDay;
        if (IsSpawned)
            DebugSetCurrentDayRpc(currentDay);
        else
            _debugDayOverrideNet.Value = currentDay;
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void DebugSetCurrentDayRpc(int currentDay) => _debugDayOverrideNet.Value = currentDay;

    public void DebugOpenTerminal()
    {
        pcActive = true;
        if (_virtualCanvasCursor != null)
            _virtualCanvasCursor.enabled = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        ShowPCBackButton();
        UIController.Instance?.ClosePlayerUI();

        if (!_navState.Value.IsActive)
            Navigate(NavAction.EnterTerminal);
    }

    // ── Public navigation API (buttons / other components call these) ───────────

    public void OpenScreen(GameObject screen) => Navigate(NavAction.OpenRootMenu);

    public void CloseAllScreens()
    {
        SetViewActive(fileListView, false);
        SetViewActive(profileView, false);
        SetViewActive(newsView, false);
    }

    public void OpenRegistry() => Navigate(NavAction.OpenRegistry);
    public void OpenResidents() => OpenActive();
    public void OpenActive() => Navigate(NavAction.OpenActive);
    public void OpenAll() => Navigate(NavAction.OpenAll);
    public void OpenDeceased() => Navigate(NavAction.OpenDeceased);
    public void OpenQuarantine() => Navigate(NavAction.OpenQuarantine);
    public void OpenNews() => Navigate(NavAction.OpenNews);

    public void OpenProfilePage(SuspectData suspectData)
    {
        int index = GetSuspectIndex(suspectData);
        if (index < 0) return;
        Navigate(NavAction.OpenProfile, suspectIndex: index);
    }

    public void OpenNewsEntryPage(TerminalNewsEntry newsEntry)
    {
        if (newsEntry == null) return;
        Navigate(NavAction.OpenNewsEntry, newsDay: newsEntry.Day);
    }

    public void OpenNextProfile() => Navigate(NavAction.NextProfile);
    public void OpenPreviousProfile() => Navigate(NavAction.PreviousProfile);

    public bool CanOpenNextProfile() => _navState.Value.Screen == TerminalScreen.Profile && _navState.Value.HasNext;
    public bool CanOpenPreviousProfile() => _navState.Value.Screen == TerminalScreen.Profile && _navState.Value.HasPrevious;

    public string GetTerminalStatus(SuspectData suspectData)
    {
        string status = GetBaseStatus(suspectData);
        if (status != "QUARANTINED" || _navState.Value.Section != TerminalSection.Quarantine)
            return status;
        int daysLeft = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.GetRemainingQuarantineDays(suspectData, GetCurrentDay()) : 0;
        return $"QUARANTINED - {daysLeft} {(daysLeft == 1 ? "DAY" : "DAYS")} LEFT";
    }

    public void FilterAF() => Navigate(NavAction.FilterLetters, filter: LetterFilter.AF);
    public void FilterGL() => Navigate(NavAction.FilterLetters, filter: LetterFilter.GL);
    public void FilterMR() => Navigate(NavAction.FilterLetters, filter: LetterFilter.MR);
    public void FilterSZ() => Navigate(NavAction.FilterLetters, filter: LetterFilter.SZ);
    public void ClearLetterFilter() => Navigate(NavAction.ClearFilter);

    // ── Request / dispatch plumbing ──────────────────────────────────────────────

    /// <summary>
    /// Entry point for every navigation intent. When networked, forwards the request to
    /// the server (which is the sole writer of <see cref="_navState"/>); every client then
    /// re-renders from the resulting synced state. When not networked (editor preview /
    /// terminal emulator scenes with no NetworkManager), applies and renders immediately.
    /// </summary>
    private void Navigate(NavAction action, int suspectIndex = -1, int newsDay = -1, LetterFilter filter = LetterFilter.None)
    {
        if (IsSpawned)
        {
            RequestNavigateRpc(action, suspectIndex, newsDay, filter);
        }
        else
        {
            ApplyNavAction(action, suspectIndex, newsDay, filter);
            ApplySyncedState(_navState.Value);
        }
    }

    [Rpc(SendTo.Server, RequireOwnership = false)]
    private void RequestNavigateRpc(NavAction action, int suspectIndex, int newsDay, LetterFilter filter) =>
        ApplyNavAction(action, suspectIndex, newsDay, filter);

    private void ApplyNavAction(NavAction action, int suspectIndex, int newsDay, LetterFilter filter)
    {
        switch (action)
        {
            case NavAction.EnterTerminal: ServerEnterTerminal(); break;
            case NavAction.ExitTerminal: ServerExitTerminal(); break;
            case NavAction.OpenRootMenu: ServerOpenRootMenu(false); break;
            case NavAction.OpenRegistry: ServerOpenRegistry(true); break;
            case NavAction.OpenAll: ServerOpenSection(TerminalSection.All, true); break;
            case NavAction.OpenActive: ServerOpenSection(TerminalSection.Active, true); break;
            case NavAction.OpenDeceased: ServerOpenSection(TerminalSection.Deceased, true); break;
            case NavAction.OpenQuarantine: ServerOpenSection(TerminalSection.Quarantine, true); break;
            case NavAction.OpenNews: ServerOpenSection(TerminalSection.News, true); break;
            case NavAction.OpenProfile: ServerOpenProfileInternal(suspectIndex, true); break;
            case NavAction.OpenNewsEntry: ServerOpenNewsEntryInternal(newsDay, true); break;
            case NavAction.NextProfile: ServerStepProfile(1); break;
            case NavAction.PreviousProfile: ServerStepProfile(-1); break;
            case NavAction.NextNews: ServerStepNews(1); break;
            case NavAction.PreviousNews: ServerStepNews(-1); break;
            case NavAction.FilterLetters: ServerApplyFilter(filter); break;
            case NavAction.ClearFilter: ServerApplyFilter(LetterFilter.None); break;
            case NavAction.Back: ServerGoBack(); break;
        }
    }

    // ── Server-authoritative navigation logic ────────────────────────────────────

    private void ServerCommit(NavSyncState state, bool pushHistory)
    {
        if (pushHistory)
            ServerPushHistory();

        state.IsActive = true;
        state.HasBack = _serverHistory.Count > 0;
        state.HasPrevious = ComputeHasPrevious(state);
        state.HasNext = ComputeHasNext(state);
        _navState.Value = state;
    }

    private void ServerPushHistory()
    {
        if (!_serverRestoring)
            _serverHistory.Push(_navState.Value);
    }

    private void ServerEnterTerminal()
    {
        _serverHistory.Clear();
        _serverRestoring = false;
        ServerOpenRootMenu(false);
    }

    private void ServerExitTerminal()
    {
        _serverHistory.Clear();
        _navState.Value = NavSyncState.Root(false);
    }

    private void ServerGoBack()
    {
        if (_serverHistory.Count == 0)
        {
            ServerExitTerminal();
            return;
        }

        NavSyncState previous = _serverHistory.Pop();
        _serverRestoring = true;
        ServerCommit(previous, false);
        _serverRestoring = false;
    }

    private void ServerOpenRootMenu(bool pushHistory) => ServerCommit(new NavSyncState
    {
        Screen = TerminalScreen.RootMenu,
        Section = TerminalSection.All,
        Filter = LetterFilter.None,
        SuspectIndex = -1,
        NewsDay = -1
    }, pushHistory);

    private void ServerOpenRegistry(bool pushHistory) => ServerCommit(new NavSyncState
    {
        Screen = TerminalScreen.RegistryMenu,
        Section = TerminalSection.All,
        Filter = LetterFilter.None,
        SuspectIndex = -1,
        NewsDay = -1
    }, pushHistory);

    private void ServerOpenSection(TerminalSection section, bool pushHistory) => ServerCommit(new NavSyncState
    {
        Screen = TerminalScreen.List,
        Section = section,
        Filter = LetterFilter.None,
        SuspectIndex = -1,
        NewsDay = -1
    }, pushHistory);

    private void ServerOpenProfileInternal(int suspectIndex, bool pushHistory)
    {
        if (suspectIndex < 0) return;
        NavSyncState next = _navState.Value;
        next.Screen = TerminalScreen.Profile;
        next.SuspectIndex = suspectIndex;
        ServerCommit(next, pushHistory);
    }

    private void ServerOpenNewsEntryInternal(int day, bool pushHistory)
    {
        NavSyncState next = _navState.Value;
        next.Screen = TerminalScreen.NewsEntry;
        next.Section = TerminalSection.News;
        next.NewsDay = day;
        ServerCommit(next, pushHistory);
    }

    private void ServerStepProfile(int direction)
    {
        NavSyncState current = _navState.Value;
        if (current.Screen != TerminalScreen.Profile) return;

        List<SuspectData> list = ApplyLetterFilter(GetSectionBaseList(current.Section), current.Filter);
        int pos = FindSuspectPosition(list, current.SuspectIndex);
        if (pos < 0) return;

        int newPos = Mathf.Clamp(pos + direction, 0, list.Count - 1);
        ServerOpenProfileInternal(GetSuspectIndex(list[newPos]), false);
    }

    private void ServerStepNews(int direction)
    {
        NavSyncState current = _navState.Value;
        if (current.Screen != TerminalScreen.NewsEntry) return;

        List<TerminalNewsEntry> entries = BuildNewsEntries();
        int pos = entries.FindIndex(e => e.Day == current.NewsDay);
        if (pos < 0) return;

        int newPos = Mathf.Clamp(pos + direction, 0, entries.Count - 1);
        ServerOpenNewsEntryInternal(entries[newPos].Day, false);
    }

    private void ServerApplyFilter(LetterFilter filter)
    {
        NavSyncState current = _navState.Value;
        if (current.Screen != TerminalScreen.List || current.Section == TerminalSection.News) return;

        NavSyncState next = current;
        next.Filter = filter;
        ServerCommit(next, false);
    }

    private bool ComputeHasPrevious(NavSyncState state)
    {
        if (state.Screen == TerminalScreen.Profile)
        {
            List<SuspectData> list = ApplyLetterFilter(GetSectionBaseList(state.Section), state.Filter);
            return FindSuspectPosition(list, state.SuspectIndex) > 0;
        }
        if (state.Screen == TerminalScreen.NewsEntry)
        {
            List<TerminalNewsEntry> entries = BuildNewsEntries();
            return entries.FindIndex(e => e.Day == state.NewsDay) > 0;
        }
        return false;
    }

    private bool ComputeHasNext(NavSyncState state)
    {
        if (state.Screen == TerminalScreen.Profile)
        {
            List<SuspectData> list = ApplyLetterFilter(GetSectionBaseList(state.Section), state.Filter);
            int pos = FindSuspectPosition(list, state.SuspectIndex);
            return pos >= 0 && pos < list.Count - 1;
        }
        if (state.Screen == TerminalScreen.NewsEntry)
        {
            List<TerminalNewsEntry> entries = BuildNewsEntries();
            int pos = entries.FindIndex(e => e.Day == state.NewsDay);
            return pos >= 0 && pos < entries.Count - 1;
        }
        return false;
    }

    // ── Client-side rendering (runs identically on every client, incl. host) ────

    private void ApplySyncedState(NavSyncState state)
    {
        SetScreenRenderCameraActive(state.IsActive);

        if (!state.IsActive)
        {
            CloseAllScreens();
            StopIdleSound();
            RefreshNavigationButtonsSynced(state);
            return;
        }

        PlayIdleSound();

        switch (state.Screen)
        {
            case TerminalScreen.RootMenu:
                ShowFileList(VillageName, $"Population: {GetPopulationAliveForTerminal()}", new List<PCListItemModel>
                {
                    new PCListItemModel("Registry", PCListItemIcon.Folder, () => Navigate(NavAction.OpenRegistry)),
                    new PCListItemModel("News", PCListItemIcon.Folder, () => Navigate(NavAction.OpenNews))
                });
                break;
            case TerminalScreen.RegistryMenu:
                ShowFileList("Registry", "Index", new List<PCListItemModel>
                {
                    new PCListItemModel("All", PCListItemIcon.Folder, () => Navigate(NavAction.OpenAll)),
                    new PCListItemModel("Active", PCListItemIcon.Folder, () => Navigate(NavAction.OpenActive)),
                    new PCListItemModel("Quarantine", PCListItemIcon.Folder, () => Navigate(NavAction.OpenQuarantine)),
                    new PCListItemModel("Deceased", PCListItemIcon.Folder, () => Navigate(NavAction.OpenDeceased))
                });
                break;
            case TerminalScreen.List:
                RenderListScreen(state);
                break;
            case TerminalScreen.Profile:
                RenderProfileScreen(state);
                break;
            case TerminalScreen.NewsEntry:
                RenderNewsEntryScreen(state);
                break;
        }

        RefreshNavigationButtonsSynced(state);
        RefreshMouseDelayed();
    }

    private void RenderListScreen(NavSyncState state)
    {
        if (state.Section == TerminalSection.News)
        {
            List<TerminalNewsEntry> entries = BuildNewsEntries();
            string summary = entries.Count > 0 ? $"NEWS ARCHIVE: {entries.Count} ISSUES" : "NEWS ARCHIVE: NO ISSUES";
            ShowFileList("News", summary, BuildNewsItems(entries));
            return;
        }

        List<SuspectData> baseList = GetSectionBaseList(state.Section);
        List<SuspectData> visibleList = ApplyLetterFilter(baseList, state.Filter);
        ShowFileList("Registry", GetSectionSummary(state.Section), BuildSuspectItems(visibleList, state.Section));
    }

    private void RenderProfileScreen(NavSyncState state)
    {
        SuspectData suspect = GetSuspectByIndex(state.SuspectIndex);
        if (suspect == null) return;

        CloseAllScreens();
        SetHeader("Profile");
        SetViewActive(profileView, true);
        profileView.Show(suspect, GetProfileEntryReason(suspect), GetProfileLastEntryDate(suspect), GetProfileStatus(suspect));
    }

    private void RenderNewsEntryScreen(NavSyncState state)
    {
        TerminalNewsEntry entry = BuildNewsEntries().Find(e => e.Day == state.NewsDay);
        if (entry == null) return;

        CloseAllScreens();
        SetHeader("News");
        SetViewActive(newsView, true);
        newsView.Show(entry);
    }

    private void ShowFileList(string headerText, string label, IReadOnlyList<PCListItemModel> items)
    {
        CloseAllScreens();
        SetHeader(headerText);
        SetViewActive(fileListView, true);
        fileListView.Show(label, items);
    }

    private List<PCListItemModel> BuildSuspectItems(IReadOnlyList<SuspectData> suspects, TerminalSection section)
    {
        var items = new List<PCListItemModel>();
        if (suspects == null) return items;
        for (int i = 0; i < suspects.Count; i++)
        {
            SuspectData suspect = suspects[i];
            if (suspect == null) continue;
            string displayName = suspect.LastName + ", " + suspect.FirstName;
            string status = GetListStatus(suspect, section);
            string text = string.IsNullOrWhiteSpace(status) ? displayName : displayName + " - " + status;
            int index = GetSuspectIndex(suspect);
            items.Add(new PCListItemModel(text, PCListItemIcon.Profile, () => Navigate(NavAction.OpenProfile, suspectIndex: index), suspect.IDPhoto));
        }
        return items;
    }

    private string GetListStatus(SuspectData suspect, TerminalSection section)
    {
        if (section == TerminalSection.Deceased)
            return GetDeathDate(suspect);

        if (section == TerminalSection.Quarantine)
        {
            int daysLeft = SuspectRunRecords.Instance != null
                ? SuspectRunRecords.Instance.GetRemainingQuarantineDays(suspect, GetCurrentDay())
                : 0;
            return $"{daysLeft} {(daysLeft == 1 ? "DAY" : "DAYS")} LEFT";
        }

        string status = GetTerminalStatus(suspect);
        return status == "CLEAR" ? string.Empty : status;
    }

    private string GetDeathDate(SuspectData suspect)
    {
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspect);
        return record != null && record.killedOnDay > 0
            ? NewspaperStartDate.AddDays(record.killedOnDay - 1).ToString("MM/dd/yy")
            : "unknown";
    }

    private List<PCListItemModel> BuildNewsItems(List<TerminalNewsEntry> entries)
    {
        var items = new List<PCListItemModel>();
        if (entries == null) return items;
        for (int i = 0; i < entries.Count; i++)
        {
            TerminalNewsEntry newsEntry = entries[i];
            string newsHeader = newsEntry?.Content != null ? newsEntry.Content.headerText : "MISSING NEWS ENTRY";
            string date = newsEntry != null ? newsEntry.Date : "UNKNOWN DATE";
            int day = newsEntry?.Day ?? -1;
            items.Add(new PCListItemModel($"{date} - {newsHeader}", PCListItemIcon.Unknown, () => Navigate(NavAction.OpenNewsEntry, newsDay: day), null, newsEntry?.Content != null));
        }
        return items;
    }

    private void RefreshNavigationButtonsSynced(NavSyncState state)
    {
        ConfigureButton(backButton, state.IsActive && state.HasBack, () => Navigate(NavAction.Back));

        bool profileScreen = state.Screen == TerminalScreen.Profile;
        bool newsScreen = state.Screen == TerminalScreen.NewsEntry;

        bool showPrevious = state.IsActive && (profileScreen || newsScreen) && state.HasPrevious;
        bool showNext = state.IsActive && (profileScreen || newsScreen) && state.HasNext;

        ConfigureButton(previousButton, showPrevious, () => Navigate(profileScreen ? NavAction.PreviousProfile : NavAction.PreviousNews));
        ConfigureButton(nextButton, showNext, () => Navigate(profileScreen ? NavAction.NextProfile : NavAction.NextNews));
    }

    private static void ConfigureButton(ClickablePCElement button, bool visible, Action onClick)
    {
        if (button == null) return;
        button.gameObject.SetActive(visible);
        button.SetClickHandler(visible ? onClick : null);
    }

    // ── Deterministic data helpers (identical results on every client) ──────────

    private List<SuspectData> GetSectionBaseList(TerminalSection section)
    {
        switch (section)
        {
            case TerminalSection.Active: return GetActiveSuspects();
            case TerminalSection.Deceased: return SortSuspects(GetAllNamedSuspects().Where(IsDeceased));
            case TerminalSection.Quarantine: return GetQuarantineSuspects();
            case TerminalSection.News: return new List<SuspectData>();
            default: return GetAllNamedSuspects();
        }
    }

    private List<SuspectData> GetQuarantineSuspects()
    {
        int day = GetCurrentDay();
        IEnumerable<SuspectData> suspects = SuspectRunRecords.Instance != null
            ? SuspectRunRecords.Instance.GetActiveQuarantineRecords(day).Where(r => r?.SuspectData != null).Select(r => r.SuspectData)
            : Enumerable.Empty<SuspectData>();
        return SortSuspects(suspects);
    }

    private static List<SuspectData> ApplyLetterFilter(List<SuspectData> baseList, LetterFilter filter)
    {
        if (baseList == null) return new List<SuspectData>();
        if (filter == LetterFilter.None) return baseList;

        (char start, char end) = GetFilterRange(filter);
        return baseList
            .Where(s => s != null && !string.IsNullOrWhiteSpace(s.LastName))
            .Where(s =>
            {
                char c = GetAlphabetFilterChar(s.LastName);
                return c >= start && c <= end;
            })
            .OrderBy(s => NormalizeForAlphabet(s.LastName)).ThenBy(s => s.FirstName).ToList();
    }

    private static (char, char) GetFilterRange(LetterFilter filter) => filter switch
    {
        LetterFilter.AF => ('A', 'F'),
        LetterFilter.GL => ('G', 'L'),
        LetterFilter.MR => ('M', 'R'),
        LetterFilter.SZ => ('S', 'Z'),
        _ => ('A', 'Z')
    };

    private int GetSuspectIndex(SuspectData suspect)
    {
        if (suspect == null || _suspectSet == null || _suspectSet.suspects == null) return -1;
        return _suspectSet.suspects.IndexOf(suspect);
    }

    private SuspectData GetSuspectByIndex(int index)
    {
        if (_suspectSet == null || _suspectSet.suspects == null || index < 0 || index >= _suspectSet.suspects.Count) return null;
        return _suspectSet.suspects[index];
    }

    private int FindSuspectPosition(List<SuspectData> list, int suspectIndex)
    {
        SuspectData target = GetSuspectByIndex(suspectIndex);
        if (target == null || list == null) return -1;
        return list.IndexOf(target);
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

    private string GetSectionSummary(TerminalSection section)
    {
        switch (section)
        {
            case TerminalSection.Quarantine:
                int day = GetCurrentDay();
                int active = SuspectRunRecords.Instance != null ? SuspectRunRecords.Instance.GetActiveQuarantineCount(day) : 0;
                int open = Mathf.Max(0, SuspectRunRecords.QuarantineSlotLimit - active);
                return $"QUARANTINE: {active}/{SuspectRunRecords.QuarantineSlotLimit} USED, {open} OPEN";
            case TerminalSection.Deceased:
                return $"DECEASED: {GetDeceasedCount()}";
            case TerminalSection.Active:
                return $"ACTIVE: {GetActiveSuspects().Count}";
            default:
                return $"RECORDS: {GetAllNamedSuspects().Count}";
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
        if (_navState.Value.HasBack)
        {
            Navigate(NavAction.Back);
            return;
        }
        ExitPC();
    }

    private void ExitPC()
    {
        pcActive = false;
        if (UIController.Instance != null) UIController.Instance.HideBackButton();
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        if (_virtualCanvasCursor != null) _virtualCanvasCursor.enabled = false;
        Navigate(NavAction.ExitTerminal);
        UIController.Instance?.ShowPlayerUI();
        if (_player == null) return;
        _player.SetCanInteract(true, "");
        _player.playerMovementController.ResetCameraPos(false, 0.5f, () => _player.playerMovementController.SetCanControl(true));
    }

    private void PlayIdleSound()
    {
        if (idleAudioSource == null || idleSfx == null)
            return;

        if (_idleFadeCoroutine != null)
            StopCoroutine(_idleFadeCoroutine);

        idleAudioSource.clip = idleSfx;
        idleAudioSource.loop = true;
        if (!idleAudioSource.isPlaying)
            idleAudioSource.Play();

        _idleFadeCoroutine = StartCoroutine(FadeIdleVolume(idleSfxVolume, idleFadeDuration, stopOnComplete: false));
    }

    private void StopIdleSound()
    {
        if (idleAudioSource == null || !idleAudioSource.isPlaying)
            return;

        if (_idleFadeCoroutine != null)
            StopCoroutine(_idleFadeCoroutine);

        _idleFadeCoroutine = StartCoroutine(FadeIdleVolume(0f, idleFadeDuration, stopOnComplete: true));
    }

    private IEnumerator FadeIdleVolume(float targetVolume, float duration, bool stopOnComplete)
    {
        float startVolume = idleAudioSource.volume;
        float elapsed = 0f;

        if (duration > 0f)
        {
            while (elapsed < duration)
            {
                elapsed += Time.deltaTime;
                idleAudioSource.volume = Mathf.Lerp(startVolume, targetVolume, elapsed / duration);
                yield return null;
            }
        }

        idleAudioSource.volume = targetVolume;

        if (stopOnComplete)
            idleAudioSource.Stop();

        _idleFadeCoroutine = null;
    }

    private void SetScreenRenderCameraActive(bool active)
    {
        if (screenRenderCamera != null)
            screenRenderCamera.SetActive(active);
    }

    private int GetCurrentDay()
    {
        int overrideDay = _debugDayOverrideNet.Value >= 0 ? _debugDayOverrideNet.Value : _debugCurrentDayOverride;
        if (overrideDay >= 0) return overrideDay;
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

    private List<SuspectData> GetActiveSuspects()
    {
        return SortSuspects(GetAllNamedSuspects().Where(IsActiveSuspect));
    }

    private bool IsActiveSuspect(SuspectData suspect)
    {
        return !IsDeceased(suspect) && !IsInQuarantine(suspect);
    }

    private bool IsInQuarantine(SuspectData suspect)
    {
        return SuspectRunRecords.Instance != null && SuspectRunRecords.Instance.IsInActiveQuarantine(suspect, GetCurrentDay());
    }

    private static List<SuspectData> SortSuspects(IEnumerable<SuspectData> suspects)
    {
        return suspects.Where(HasTerminalName).OrderBy(s => NormalizeForAlphabet(s.LastName)).ThenBy(s => s.FirstName).ToList();
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
}
