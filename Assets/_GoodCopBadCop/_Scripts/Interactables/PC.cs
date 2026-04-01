using System;
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
    bool pcActive = false;
    private PlayerInteractionController _player;
    [SerializeField] private SimpleCanvasCursorFromMouseDelta _virtualCanvasCursor;

    [Header("Screens")] 
    [SerializeField] private GameObject[] screens;
    [SerializeField] private GameObject mainScreen;
    
    void Start()
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
        UIController.Instance.ShowBackUI(true);
        
        player.playerMovementController.LookAtTarget(lookAtTarget.transform);
        player.transform.DOMove(standPos.position, .5f);
        player.transform.DORotate(standPos.rotation.eulerAngles, .5f);
        pcActive = true;
        _player = player;
        _virtualCanvasCursor.enabled = true;
        OpenScreen(mainScreen);
    }

    private void Update()
    {
        if (pcActive)
        {
            if (Input.GetKeyDown(KeyCode.Q))
            {
                pcActive = false;
                UIController.Instance.ShowBackUI(false);
                _player.playerMovementController.SetCanControl(true);
                _player.SetCanInteract(true, "");
            }
        }
    }

    public void OpenScreen(GameObject screen)
    {
        for (int i = 0; i < screens.Length; i++)
        {
            screens[i].SetActive(false);
            if (screens[i] == screen) screens[i].SetActive(true);
        }
        
        _virtualCanvasCursor.SetScreenContent(screen.transform);
    }

    public void CloseAllScreens()
    {
        for (int i = 0; i < screens.Length; i++)
        {
            screens[i].SetActive(false);
        }
    }

    public void SortScreen()
    {
        
    }
}

public static class SuspectSorter
{
    // 🔹 FILTER BY STATUS
    public static List<SuspectRecord> FilterByStatus(List<SuspectRecord> input, CharacterStatus status)
    {
        return input.Where(r => r.Status == status).ToList();
    }

    // 🔹 RECENT EXITS (sorted by time DESC)
    public static List<SuspectRecord> SortByRecentExit(List<SuspectRecord> input, int max = 20)
    {
        return input
            .Where(r => r.LastExitTime != DateTime.MinValue)
            .OrderByDescending(r => r.LastExitTime)
            .Take(max)
            .ToList();
    }

    // 🔹 ALPHABET RANGE (A-F, G-L, etc)
    public static List<SuspectRecord> FilterByLastNameRange(List<SuspectRecord> input, char start, char end)
    {
        return input
            .Where(r =>
            {
                if (string.IsNullOrEmpty(r.Data.LastName)) return false;

                char first = char.ToUpper(r.Data.LastName[0]);
                return first >= start && first <= end;
            })
            .OrderBy(r => r.Data.LastName)
            .ToList();
    }

    // 🔹 FULL ALPHABET SORT (A-Z)
    public static List<SuspectRecord> SortAlphabetical(List<SuspectRecord> input)
    {
        return input
            .OrderBy(r => r.Data.LastName)
            .ThenBy(r => r.Data.FirstName)
            .ToList();
    }
}