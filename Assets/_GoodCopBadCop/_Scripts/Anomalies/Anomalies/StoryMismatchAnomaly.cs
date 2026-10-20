/// <summary>
/// Mental anomaly that causes the suspect to give a verbal answer that contradicts
/// either their document data or something they stated previously.
///
/// No visual activation is needed - the anomaly's presence in AnomalyController.activeAnomalies
/// is sufficient. SuspectCharacter.GetQuestionResponse checks for this anomaly at runtime
/// and serves the mismatchAnswer from SuspectData.QuestionResponseSet instead of
/// the normal answer when this anomaly is active.
///
/// Mismatch answers are authored per character in their SuspectData ScriptableObject asset.
/// </summary>
public class StoryMismatchAnomaly : DocumentationAnomaly
{
}
