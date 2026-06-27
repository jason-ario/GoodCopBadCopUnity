using System.Collections;
using UnityEngine;

public class Vaccine : PickableObject
{
    private const string USE_SYRINGE_TRIGGER = "UseSyringe";

    [SerializeField] private float useAnimationDuration = 2f;

    /// <summary>
    /// Initiates the syringe-use sequence: triggers the player's UseSyringe animation,
    /// waits for it to complete, removes one of the suspect's active anomalies,
    /// then despawns this syringe.
    /// </summary>
    public void UseSyringe(SuspectCharacter suspect)
    {
        if (isUsing) return;
        StartCoroutine(SyringeSequenceRoutine(suspect));
    }

    private IEnumerator SyringeSequenceRoutine(SuspectCharacter suspect)
    {
        isUsing = true;
        playerPickupController.PlayerAnimationController.SetAnimTrigger(USE_SYRINGE_TRIGGER);

        yield return new WaitForSeconds(useAnimationDuration);

        suspect.ReceiveVaccine();
        DespawnServerRpc();
    }
}
