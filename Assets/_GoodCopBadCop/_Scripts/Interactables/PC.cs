using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
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

        // Resolve the status stamp shown on the profile from the runtime suspect record.
        string status = string.Empty;
        SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(suspectData);
        if (record != null)
        {
            if (record.isReplacement)
                status = "REPLACED";
            else if (record.isKilled)
                status = "DECEASED";
        }

        profilePage.SetProfileData(suspectData, status: status);

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
        _currentBaseList = _suspectSet.suspects
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
        SelectFolderTab(0);
    }

    public void OpenVisitors()
    {
        _currentBaseList = _suspectSet.suspects
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        RenderCurrentList();
        OpenScreen(suspectListScreen);
        SelectFolderTab(0);
    }

    public void OpenDeceased()
    {
        // Filter to suspects that have been killed this run.
        _currentBaseList = _suspectSet.suspects
            .Where(s =>
            {
                if (s == null) return false;
                SuspectRecord record = SuspectRunRecords.Instance?.GetRecord(s);
                return record != null && record.isKilled;
            })
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
        ClearCurrentProfileSelection();

        RenderCurrentList();
        OpenScreen(suspectListScreen);
        SelectFolderTab(0);
    }

    public void OpenAll()
    {
        _currentBaseList = _suspectSet.suspects
            .OrderBy(s => s.LastName)
            .ThenBy(s => s.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectData>(_currentBaseList);
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