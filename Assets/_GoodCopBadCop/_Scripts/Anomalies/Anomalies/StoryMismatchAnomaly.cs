using UnityEngine;

/// <summary>
/// Mental anomaly that causes the suspect to give a verbal answer that contradicts
/// either their document data or something they stated in a previous day band.
/// 
/// No visual activation is needed — the anomaly's presence in AnomalyController.activeAnomalies
/// is sufficient. SuspectCharacter.GetQuestionResponse checks for this anomaly at runtime
/// and serves the mismatchXxxDaysAnswer from SuspectData.QuestionResponseSet instead of
/// the normal day-band answer when this anomaly is active.
/// 
/// Mismatch answers are authored per character in their SuspectData ScriptableObject asset.
/// </summary>
public class StoryMismatchAnomaly : DocumentationAnomaly
{
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
    }
}
