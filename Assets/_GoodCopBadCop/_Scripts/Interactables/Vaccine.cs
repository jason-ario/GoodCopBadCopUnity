using System.Collections;
using UnityEngine;

public class Vaccine : PickableObject
{
    private const string USING_TOOL_BOOL = "UsingTool";

    [SerializeField] private float useAnimationDuration = 2f;
    [SerializeField] private float usingToolPulseDuration = 0.1f;

    /// <summary>
    /// Initiates the syringe-use sequence: pulses the UsingTool bool, waits for the
    /// animation to finish, removes one of the suspect's active anomalies, then despawns.
    /// </summary>
    public void UseSyringe(SuspectCharacter suspect)
    {
        if (isUsing) return;
        StartCoroutine(SyringeSequenceRoutine(suspect));
    }

    private IEnumerator SyringeSequenceRoutine(SuspectCharacter suspect)
    {
        isUsing = true;

        playerPickupController.PlayerAnimationController.SetAnimBool(USING_TOOL_BOOL, true);
        yield return new WaitForSeconds(usingToolPulseDuration);
        playerPickupController.PlayerAnimationController.SetAnimBool(USING_TOOL_BOOL, false);

        yield return new WaitForSeconds(useAnimationDuration - usingToolPulseDuration);

        suspect.ReceiveVaccine();
        DespawnServerRpc();
    }
}
