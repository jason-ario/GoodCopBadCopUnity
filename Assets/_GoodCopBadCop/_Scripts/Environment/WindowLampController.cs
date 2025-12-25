using UnityEngine;

public class WindowLampController : MonoBehaviour
{
    [SerializeField] Light lightComponent;
    [SerializeField] Color greenLightColor;
    [SerializeField] Color redLightColor;
    [SerializeField] Material greenMaterial;
    [SerializeField] Material redMaterial;
    [SerializeField] private MeshRenderer lightMeshRenderer;
    
    [ContextMenu("Turn Green")]
    public void TurnGreen()
    {
        lightMeshRenderer.material = greenMaterial;
        lightComponent.color = greenLightColor;
    }
    
    [ContextMenu("Turn Red")]
    public void TurnRed()
    {
        lightMeshRenderer.material = redMaterial;
        lightComponent.color = redLightColor;
    }
}
