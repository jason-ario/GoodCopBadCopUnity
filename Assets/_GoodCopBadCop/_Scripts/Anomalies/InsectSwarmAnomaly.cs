using System.Collections;
using GoodCopBadCop.RoomSystem;
using UnityEngine;

/// <summary>
/// Supernatural anomaly where the suspect is accompanied by or generates an insect swarm.
/// Once activated, waits for the suspect to arrive at the booth window, then triggers a
/// violent cockroach swarm inside the booth after a single random delay. Fires only once
/// per visit — the event subscription is dropped immediately after the delay starts.
/// </summary>
public class InsectSwarmAnomaly : SupernaturalAnomaly
{
    [Tooltip("Scene-level controller that owns the cockroach spawn area and drives the swarm.")]
    [SerializeField] private CockroachSpawner _cockroachSpawner;

    [Header("Trigger Delay")]
    [Tooltip("Minimum seconds after the suspect arrives at the booth before the swarm triggers.")]
    [SerializeField] private float _minDelay = 10f;

    [Tooltip("Maximum seconds after the suspect arrives at the booth before the swarm triggers.")]
    [SerializeField] private float _maxDelay = 40f;

    [VContainer.Inject] private IRoomService roomService;

    private Coroutine _swarmCoroutine;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (roomService == null && _cockroachSpawner == null)
        {
            Debug.LogWarning("[InsectSwarmAnomaly] RoomService was not injected and CockroachSpawner is not assigned.", this);
            return;
        }

        SuspectController.OnSuspectArrived += OnSuspectArrived;
    }

    public override void DeactivateAnomaly()
    {
        base.DeactivateAnomaly();
        Cleanup();
    }

    public override void InitializeDisabled()
    {
        Cleanup();
    }

    private void OnSuspectArrived(int suspectIndex)
    {
        // Unsubscribe immediately — the swarm fires exactly once per visit.
        SuspectController.OnSuspectArrived -= OnSuspectArrived;
        _swarmCoroutine = StartCoroutine(TriggerSwarmAfterDelay());
    }

    private IEnumerator TriggerSwarmAfterDelay()
    {
        yield return new WaitForSeconds(Random.Range(_minDelay, _maxDelay));
        if (roomService != null)
        {
            roomService.StartInsectSwarm();
        }
        else
        {
            _cockroachSpawner.StartSwarm();
        }

        _swarmCoroutine = null;
    }

    private void Cleanup()
    {
        SuspectController.OnSuspectArrived -= OnSuspectArrived;

        if (_swarmCoroutine != null)
        {
            StopCoroutine(_swarmCoroutine);
            _swarmCoroutine = null;
        }

        if (roomService != null)
        {
            roomService.StopInsectSwarm();
        }
        else
        {
            _cockroachSpawner?.StopSwarm();
        }
    }

    [ContextMenu("Activate Anomaly")]
    private void ActivateAnomalyDebug() => ActivateAnomaly();
}
