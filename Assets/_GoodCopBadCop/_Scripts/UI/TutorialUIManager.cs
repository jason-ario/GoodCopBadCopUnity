using System;
using TMPro;
using UnityEngine;

public class TutorialUIManager : MonoBehaviour
{
    public static TutorialUIManager Instance;
    [SerializeField] private TextMeshProUGUI tutorialText;

    private void Awake()
    {
        Instance = this;
    }
    
    public void SetTutorialText(string text)
    {
        tutorialText.text = text;
        tutorialText.gameObject.SetActive(true);
    }
    
    public void HideTutorialText()
    {
        tutorialText.gameObject.SetActive(false);
    }
}
