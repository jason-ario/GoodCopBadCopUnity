using Unity.Netcode;
using UnityEngine;


public class WindowLampController : NetworkBehaviour
{
    [SerializeField] Light lightComponent;
    [SerializeField] Color greenLightColor;
    [SerializeField] Color redLightColor;
    [SerializeField] Material greenMaterial;
    [SerializeField] Material redMaterial;
    [SerializeField] private MeshRenderer lightMeshRenderer;

    /// <summary>Turns the lamp green on all clients. Must be called from the server.</summary>
    [ContextMenu("Turn Green")]
    public void TurnGreen()
    {
        ApplyGreen();
        if (IsSpawned)
            TurnGreenClientRpc();
    }

    [ClientRpc]
    private void TurnGreenClientRpc()
    {
        ApplyGreen();
    }

    /// <summary>Turns the lamp red on all clients. Must be called from the server.</summary>
    [ContextMenu("Turn Red")]
    public void TurnRed()
    {
        ApplyRed();
        if (IsSpawned)
            TurnRedClientRpc();
    }

    [ClientRpc]
    private void TurnRedClientRpc()
    {
        ApplyRed();
    }

    private void ApplyGreen()
    {
        lightMeshRenderer.material = greenMaterial;
        lightComponent.color = greenLightColor;
    }

    private void ApplyRed()
    {
        lightMeshRenderer.material = redMaterial;
        lightComponent.color = redLightColor;
    }
}
