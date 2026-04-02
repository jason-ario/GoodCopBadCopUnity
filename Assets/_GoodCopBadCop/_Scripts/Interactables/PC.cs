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
    private SuspectDatabase suspectDatabase;

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
    [SerializeField] SimpleCanvasCursorFromMouseDelta mouseCursor;
    [SerializeField] ClickablePCScrollbar PCScrollbar;
    

    private void Start()
    {
        CloseAllScreens();
    }

    protected override void Awake()
    {
        base.Awake();
        suspectDatabase = SuspectDatabase.Instance;
    }

    public override void Interact(PlayerInteractionController player)
    {
        base.Interact(player);

        player.playerMovementController.SetCanControl(false);
        player.SetCanInteract(false, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Confined;
        UIController.Instance.ShowBackUI(true);

        player.playerMovementController.LookAtTarget(lookAtTarget.transform);
        player.transform.DOMove(standPos.position, 0.5f);
        player.transform.DORotate(standPos.rotation.eulerAngles, 0.5f);

        pcActive = true;
        _player = player;
        _virtualCanvasCursor.enabled = true;

        OpenScreen(mainScreen);

        // Optional: clear list state when opening the PC
        _currentBaseList.Clear();
        _currentVisibleList.Clear();
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
        UIController.Instance.ShowBackUI(false);

        _player.playerMovementController.SetCanControl(true);
        _player.SetCanInteract(true, "");

        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;

        _virtualCanvasCursor.enabled = false;
        CloseAllScreens();
    }

    public void OpenScreen(GameObject screen)
    { 
        CloseAllScreens();
        suspectListScreen.SetActive(screen == suspectListScreen);  
        mainScreen.SetActive(screen == mainScreen);
        mouseCursor.SetScreenContent();
        StartCoroutine(WaitAndRefreshMouse());
    }

    IEnumerator WaitAndRefreshMouse()
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
        CloseAllScreens();
        profilePage.gameObject.SetActive(true);
        profilePage.SetProfileData(suspectData);
        StartCoroutine(WaitAndRefreshMouse());
    }
    
    // --------------------------------------------------
    // CATEGORY / FOLDER BUTTONS
    // --------------------------------------------------

    public void OpenResidents()
    {
        _currentBaseList = suspectDatabase
            .GetAllRecords()
            .Where(r => r.Status == CharacterStatus.Resident)
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        OpenScreen(suspectListScreen);
        FilterAF();
        RenderCurrentList();
    }

    public void OpenVisitors()
    {
        _currentBaseList = suspectDatabase
            .GetAllRecords()
            .Where(r => r.Status == CharacterStatus.Visitor)
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        RenderCurrentList();
        OpenScreen(suspectListScreen);
    }

    public void OpenDeceased()
    {
        _currentBaseList = suspectDatabase
            .GetAllRecords()
            .Where(r => r.Status == CharacterStatus.Deceased)
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        RenderCurrentList();
        OpenScreen(suspectListScreen);
    }

    public void OpenRecentExits()
    {
        _currentBaseList = suspectDatabase
            .GetAllRecords()
            .Where(r => r.LastExitTime != DateTime.MinValue)
            .OrderByDescending(r => r.LastExitTime)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        RenderCurrentList();
        OpenScreen(suspectListScreen);
    }

    // Optional fallback if you want "all suspects"
    public void OpenAll()
    {
        _currentBaseList = suspectDatabase
            .GetAllRecords()
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();

        _currentVisibleList = new List<SuspectRecord>(_currentBaseList);
        RenderCurrentList();
        OpenScreen(suspectListScreen);
    }

    // --------------------------------------------------
    // LETTER CHUNK BUTTONS
    // --------------------------------------------------

    public void FilterAF()
    {
        FilterByLastNameRange('A', 'F');
        PCScrollbar.ResetToTop();
    }

    public void FilterGL()
    {
        FilterByLastNameRange('G', 'L');
        PCScrollbar.ResetToTop();
    }

    public void FilterMR()
    {
        FilterByLastNameRange('M', 'R');
        PCScrollbar.ResetToTop();
    }

    public void FilterSZ()
    {
        FilterByLastNameRange('S', 'Z');
        PCScrollbar.ResetToTop();
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

    // Optional getter if another script needs the current list
    public List<SuspectRecord> GetCurrentVisibleList()
    {
        return _currentVisibleList;
    }
}