using System.Collections;
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

    private Coroutine _swarmCoroutine;

    public override void ActivateAnomaly()
    {
        base.ActivateAnomaly();

        if (_cockroachSpawner == null)
        {
            Debug.LogWarning("[InsectSwarmAnomaly] CockroachSpawner is not assigned.", this);
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
        _cockroachSpawner.StartSwarm();
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

        _cockroachSpawner?.StopSwarm();
    }

    [ContextMenu("Activate Anomaly")]
    private void ActivateAnomalyDebug() => ActivateAnomaly();
}
