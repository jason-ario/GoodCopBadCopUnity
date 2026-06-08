using UnityEngine;
using Unity.Netcode;
using UnityEngine.AI;

/// <summary>
/// Debug utility to spawn a swarm of aggroed mutants.
/// Press 'K' to spawn 5 mutants forced-aggroed to the booth.
/// </summary>
public class MutantDebugSpawner : NetworkBehaviour
{
    [Header("Configuration")]
    [SerializeField] private GameObject mutantPrefab;
    [SerializeField] private KeyCode spawnHotkey = KeyCode.K;
    [SerializeField] private int swarmSize = 5;
    [SerializeField] private float spawnRadius = 20f;

    private Transform _boothTransform;

    private void Update()
    {
        if (!IsServer || !Input.GetKeyDown(spawnHotkey)) return;

        if (_boothTransform == null)
        {
            // Find the booth via SuspectController (the brain of the booth area)
            SuspectController controller = Object.FindAnyObjectByType<SuspectController>();
            if (controller != null)
                _boothTransform = controller.transform;
        }

        if (mutantPrefab == null)
        {
            Debug.LogError("[MutantDebugSpawner] Mutant Prefab not assigned!", this);
            return;
        }

        if (_boothTransform == null)
        {
            Debug.LogError("[MutantDebugSpawner] Could not find booth (SuspectController) in scene!", this);
            return;
        }

        SpawnSwarm();
    }

    private void SpawnSwarm()
    {
        Debug.Log($"[MutantDebugSpawner] Spawning swarm of {swarmSize} mutants aggroed to {_boothTransform.name}...");

        for (int i = 0; i < swarmSize; i++)
        {
            // Pick a random spot roughly 20m away from the booth
            Vector2 randomCircle = Random.insideUnitCircle.normalized * spawnRadius;
            Vector3 spawnPos = _boothTransform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
            
            // Snap to NavMesh
            if (NavMesh.SamplePosition(spawnPos, out NavMeshHit hit, 5f, NavMesh.AllAreas))
            {
                GameObject go = Instantiate(mutantPrefab, hit.position, Quaternion.identity);
                NetworkObject netObj = go.GetComponent<NetworkObject>();
                
                MutantEnemy enemy = go.GetComponent<MutantEnemy>();
                if (enemy != null)
                {
                    enemy.SetAggroTarget(_boothTransform);
                    enemy.SetForceAggro(true);
                }

                netObj.Spawn();
            }
        }
    }
}
