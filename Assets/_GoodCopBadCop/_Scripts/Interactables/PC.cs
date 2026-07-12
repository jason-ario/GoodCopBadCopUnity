using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using GoodCopBadCop.SuspectPaperwork;
using UnityEngine;

public class PC : Interactable
{
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
        UIController.Instance.HideBackButton();
        
        _player.SetCanInteract(true, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        _virtualCanvasCursor.enabled = false;
        
        _player.playerMovementController.ResetCameraPos(false, 0.5f, () => _player.playerMovementController.SetCanControl(true));
    }

    public void OpenScreen(GameObject screen)
    {
        CloseAllScreens();

        suspectListScreen.SetActive(screen == suspectListScreen);
        mainScreen.SetActive(screen == mainScreen);

        mouseCursor.SetScreenContent();
        StartCoroutine(WaitAndRefreshMouse());
    }

    private IEnumerator WaitAndRefreshMouse()
    {
        yield return new WaitForEndOfFrame();
        yield return new WaitForEndOfFrame();
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

    public void OpenResidents()
    {
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

    public void OpenDeceased()
    {
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
        => CampaignManager.Instance != null ? CampaignManager.Instance.CurrentDay : -1;

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
        terminalRecordListUI.ShowRecords(_currentVisibleList);
        StartCoroutine(WaitAndRefreshMouse());
    }
}