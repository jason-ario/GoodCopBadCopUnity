using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;

public class PC : Interactable
{
    [Header("Data")]
    [SerializeField] private SuspectSet allSuspects;

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

    private List<SuspectRecord> _currentBaseList = new();
    private List<SuspectRecord> _currentVisibleList = new();

    [SerializeField] private SimpleCanvasCursorFromMouseDelta mouseCursor;
    [SerializeField] private ClickablePCScrollbar PCScrollbar;

    // Profile navigation state
    private SuspectData _currentProfileSuspect;
    private int _currentProfileIndex = -1;
    [SerializeField] private PCFolderTab[] _folderTabs;
    private bool isOn;

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

        pcActive = true;
        _player = player;
        _virtualCanvasCursor.enabled = true;

        if (!isOn)
        {
            isOn = true;
            OpenScreen(mainScreen);
        }

        // Clear list/profile state when opening the PC
        _currentBaseList.Clear();
        _currentVisibleList.Clear();
        ClearCurrentProfileSelection();
    }

    private void Update()
    {
        if (!pcActive) return;

        if (Input.GetKeyDown(KeyCode.Q))
        {
            ExitPC();
        }
    }

    private void ExitPC()
    {
        pcActive = false;
        UIController.Instance.HideBackButton();

        _player.playerMovementController.SetCanControl(true);
        _player.SetCanInteract(true, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        _virtualCanvasCursor.enabled = false;
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
        profilePage.SetProfileData(suspectData);

        UpdateProfileNavigationUI(); // 🔥 IMPORTANT

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
        if (_currentBaseList == null || _currentBaseList.Count == 0)
            return;

        if (_currentProfileSuspect == null)
            return;

        if (_currentProfileIndex < 0)
            _currentProfileIndex = GetBaseListIndex(_currentProfileSuspect);

        if (_currentProfileIndex < 0)
            return;

        int nextIndex = _currentProfileIndex + 1;

        if (nextIndex >= _currentBaseList.Count)
            nextIndex = _currentBaseList.Count - 1;

        SuspectRecord nextRecord = _currentBaseList[nextIndex];
        if (nextRecord == null || nextRecord.Data == null)
            return;

        OpenProfilePage(nextRecord.Data); // already updates UI
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

        SuspectRecord previousRecord = _currentBaseList[previousIndex];
        if (previousRecord == null || previousRecord.Data == null)
            return;

        OpenProfilePage(previousRecord.Data);
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
            SuspectRecord record = _currentBaseList[i];

            if (record == null || record.Data == null)
                continue;

            // Best case: same object reference
            if (record.Data == suspectData)
                return i;

            // Fallback: identify by core fields
            if (AreSameSuspect(record.Data, suspectData))
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
        _currentBaseList = SuspectDatabase.Instance
            .GetAllRecords()
            .Where(r => r.Status == CharacterStatus.Resident)
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        ClearCurrentProfileSelection();

        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenVisitors()
    {
        _currentBaseList = SuspectDatabase.Instance
            .GetAllRecords()
            .Where(r => r.Status == CharacterStatus.Visitor)
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        ClearCurrentProfileSelection();

        RenderCurrentList();
        OpenScreen(suspectListScreen);
        SelectFolderTab(0);
    }

    public void OpenDeceased()
    {
        _currentBaseList = SuspectDatabase.Instance
            .GetAllRecords()
            .Where(r => r.Status == CharacterStatus.Deceased)
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        ClearCurrentProfileSelection();

        RenderCurrentList();
        OpenScreen(suspectListScreen);
        SelectFolderTab(0);
    }

    public void OpenRecentExits()
    {
        _currentBaseList = SuspectDatabase.Instance
            .GetAllRecords()
            .Where(r => r.LastExitTime != DateTime.MinValue)
            .OrderByDescending(r => r.LastExitTime)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        ClearCurrentProfileSelection();

        RenderCurrentList();
        OpenScreen(suspectListScreen);
        SelectFolderTab(0);
    }

    public void OpenAll()
    {
        _currentBaseList = SuspectDatabase.Instance
            .GetAllRecords()
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        ClearCurrentProfileSelection();

        RenderCurrentList();
        OpenScreen(suspectListScreen);
        SelectFolderTab(0);
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
        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        RenderCurrentList();
    }

    private void FilterByLastNameRange(char start, char end)
    {
        if (_currentBaseList == null || _currentBaseList.Count == 0)
        {
            _currentVisibleList = new List<SuspectRecord>();
            RenderCurrentList();
            return;
        }

        _currentVisibleList = _currentBaseList
            .Where(r =>
            {
                if (r == null || r.Data == null || string.IsNullOrWhiteSpace(r.Data.LastName))
                    return false;

                string trimmedLastName = r.Data.LastName.Trim();
                char firstChar = char.ToUpper(trimmedLastName[0]);

                return firstChar >= start && firstChar <= end;
            })
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
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

    public List<SuspectRecord> GetCurrentVisibleList()
    {
        return _currentVisibleList;
    }
}