using UnityEngine;

public class NameWrong : DocumentationAnomaly
{
    [SerializeField] SuspectController suspect;
    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
    }
}
