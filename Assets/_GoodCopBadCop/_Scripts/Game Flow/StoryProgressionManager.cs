using System;
using UnityEngine;

public class StoryProgressionManager : MonoBehaviour
{
    [SerializeField] private IntroTutorialManager _introTutorialManager;
    public static StoryProgressionManager Instance;

    private void Awake()
    {
        Instance = this;
    }

    public void StartGame()
    {
        Debug.Log("Starting Game");
        StartIntroTutorial();

        if (SaveDataManager.Instance.HasSeenIntroTutorial == false)
        {
        }
    }

    private void StartIntroTutorial()
    {
        _introTutorialManager.StartIntroTutorial();
        SaveDataManager.Instance.HasSeenIntroTutorial = true;
    }
}
