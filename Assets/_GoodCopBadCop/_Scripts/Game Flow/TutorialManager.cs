using UnityEngine;

public class TutorialManager : MonoBehaviour
{
    public static TutorialManager Instance; 

    void Awake()
    {
        Instance = this;
    }
    
    public void StartTutorial()
    {
        
    }
}
