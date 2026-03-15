using UnityEngine;

public class TumorGrowthAnomaly : MutationAnomaly
{
    [SerializeField] GameObject tumor;
    [SerializeField] SkinnedMeshRenderer skinnedMeshRendererToChange;
    [SerializeField] Material materialToChangeTo;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();
        skinnedMeshRendererToChange.material = materialToChangeTo;
        tumor.SetActive(true);
    }
}
