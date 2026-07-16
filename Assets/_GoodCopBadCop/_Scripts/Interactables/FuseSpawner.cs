using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-only. Spawns <see cref="FusePickup"/> objects at random pre-specified
/// positions in the power station whenever a fuse-required power outage begins,
/// and cleans them up when power is restored.
///
/// Setup notes:
///   - Assign <see cref="_electricityController"/>.
///   - Assign one or more fuse prefabs to <see cref="_fusePrefabs"/>. If you assign
///     three (Red/Green/Blue), each spawned fuse will cycle through them in order.
///     If you assign one, all spawned fuses use it.
///   - Add child GameObjects or empty Transforms in the power station as spawn point
///     candidates and assign them to <see cref="_spawnPoints"/>.
///   - Set <see cref="_fuseCount"/> to the number of fuses to scatter per outage
///     (must be ≤ number of spawn points).
///   - All fuse prefabs must be registered in the NetworkManager's Network Prefab List.
/// </summary>
public class FuseSpawner : NetworkBehaviour
{
    [Header("References")]
    [SerializeField] private ElectricityController _electricityController;

    [Header("Fuse Prefabs")]
    [Tooltip("Fuse pickup prefab(s). Multiple entries cycle across spawned fuses for visual variety.")]
    [SerializeField] private GameObject[] _fusePrefabs;

    [Header("Spawn Points")]
    [Tooltip("Candidate positions inside the power station. A random subset is chosen each outage.")]
    [SerializeField] private Transform[] _spawnPoints;

    [Tooltip("How many fuses to scatter per outage. Clamped to the number of available spawn points.")]
    [SerializeField] private int _fuseCount = 3;

    // ── Runtime state (server-only) ───────────────────────────────────────────

    private readonly List<NetworkObject> _activeFuses = new();

    /// <summary>True while a fuse-required outage is active and fuses are in the world.</summary>
    private bool _outageActive = false;

    // ── Lifecycle ─────────────────────────────────────────────────────────────

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;

        _electricityController.OnFuseRequiredOutageStarted += OnOutageStarted;
        _electricityController.OnFuseOutageResolved        += OnOutageResolved;
    }

    public override void OnNetworkDespawn()
    {
        if (_electricityController == null) return;

        _electricityController.OnFuseRequiredOutageStarted -= OnOutageStarted;
        _electricityController.OnFuseOutageResolved        -= OnOutageResolved;
    }

    // ── Event handlers ────────────────────────────────────────────────────────

    private void OnOutageStarted()
    {
        if (_outageActive)
        {
            Debug.LogWarning("[FuseSpawner] Outage already active — skipping duplicate spawn.");
            return;
        }

        SpawnFuses();
    }

    private void OnOutageResolved()
    {
        DespawnUncollectedFuses();
        _outageActive = false;
    }

    // ── Spawning ──────────────────────────────────────────────────────────────

    private void SpawnFuses()
    {
        if (_fusePrefabs == null || _fusePrefabs.Length == 0)
        {
            Debug.LogWarning("[FuseSpawner] No fuse prefabs assigned — cannot spawn.");
            return;
        }

        if (_spawnPoints == null || _spawnPoints.Length == 0)
        {
            Debug.LogWarning("[FuseSpawner] No spawn points assigned — cannot spawn.");
            return;
        }

        int count = Mathf.Min(_fuseCount, _spawnPoints.Length);

        // Fisher-Yates shuffle on a working copy to pick a random subset without repeats.
        Transform[] pool = (Transform[])_spawnPoints.Clone();
        for (int i = pool.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (pool[i], pool[j]) = (pool[j], pool[i]);
        }

        for (int i = 0; i < count; i++)
        {
            GameObject prefab = _fusePrefabs[i % _fusePrefabs.Length];
            if (prefab == null)
            {
                Debug.LogWarning($"[FuseSpawner] _fusePrefabs[{i % _fusePrefabs.Length}] is null — skipping slot {i}.");
                continue;
            }

            Transform point = pool[i];
            GameObject go   = Instantiate(prefab, point.position, point.rotation);
            NetworkObject netObj = go.GetComponent<NetworkObject>();

            if (netObj == null)
            {
                Debug.LogError($"[FuseSpawner] Prefab '{prefab.name}' has no NetworkObject component. Ensure it is registered in the NetworkManager prefab list.");
                Destroy(go);
                continue;
            }

            netObj.Spawn(destroyWithScene: true);
            _activeFuses.Add(netObj);
        }

        _outageActive = true;
        Debug.Log($"[FuseSpawner] Spawned {_activeFuses.Count} fuse(s) across the power station.");
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Despawns tracked fuses that have not been inserted into a fuse slot.
    /// Fuses currently seated in a slot are kept alive — the slot holds the reference.
    /// Server-only.
    /// </summary>
    private void DespawnUncollectedFuses()
    {
        int despawned = 0;

        foreach (NetworkObject netObj in _activeFuses)
        {
            if (netObj == null || !netObj.IsSpawned) continue;

            // A fuse inserted into a FuseSlot has its colliders locked (interactable locked)
            // and is parent-constrained to the slot. We use IsHeld as a proxy: seated fuses
            // have _holdingClientId == ulong.MaxValue (not held) but their NetworkTransform
            // is disabled. The safest heuristic: if a PickableObject is not held AND is not
            // being held by any client, it is either in a slot or still on the ground.
            // Only despawn fuses that are on the ground (no constraint, NT still enabled).
            if (netObj.TryGetComponent<FusePickup>(out var fuse) && fuse.IsHeld)
            {
                // Being actively carried — leave it in the player's hands.
                continue;
            }

            // Check if seated in a slot: a seated fuse has NetworkTransform disabled.
            // If NT is disabled but not held, it is in a slot — do not despawn.
            var nt = netObj.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            if (nt != null && !nt.enabled)
            {
                // Seated in a slot — skip.
                continue;
            }

            NetworkHelper.Despawn(netObj);
            despawned++;
        }

        _activeFuses.Clear();
        Debug.Log($"[FuseSpawner] Outage resolved — despawned {despawned} uncollected fuse(s).");
    }

    /// <summary>
    /// Immediately despawns ALL tracked fuses regardless of state.
    /// Server-only utility for forced cleanup (e.g. at shift end).
    /// </summary>
    public void DespawnAll()
    {
        if (!IsServer) return;

        foreach (NetworkObject netObj in _activeFuses)
        {
            if (netObj != null && netObj.IsSpawned)
                NetworkHelper.Despawn(netObj);
        }

        _activeFuses.Clear();
        _outageActive = false;
        Debug.Log("[FuseSpawner] DespawnAll: all tracked fuses removed.");
    }

    // ── Gizmos ────────────────────────────────────────────────────────────────

    // Cached at edit time so repeated OnDrawGizmos calls don't re-query.
    [System.NonSerialized] private Mesh   _gizmoMesh;
    [System.NonSerialized] private bool   _gizmoMeshResolved;
    [System.NonSerialized] private Vector3    _gizmoMeshScale;
    [System.NonSerialized] private Vector3    _gizmoChildOffset;

    /// <summary>
    /// Lazily resolves the fuse mesh and its world-space scale from the first assigned
    /// fuse prefab. Results are cached so the hierarchy is only traversed once per
    /// editor session / domain reload.
    /// </summary>
    private bool TryGetFuseGizmoData(out Mesh mesh, out Vector3 meshScale, out Vector3 childLocalOffset)
    {
        if (_gizmoMeshResolved)
        {
            mesh             = _gizmoMesh;
            meshScale        = _gizmoMeshScale;
            childLocalOffset = _gizmoChildOffset;
            return _gizmoMesh != null;
        }

        _gizmoMeshResolved = true;
        mesh             = null;
        meshScale        = Vector3.one;
        childLocalOffset = Vector3.zero;

        if (_fusePrefabs == null || _fusePrefabs.Length == 0 || _fusePrefabs[0] == null)
            return false;

        MeshFilter mf = _fusePrefabs[0].GetComponentInChildren<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return false;

        _gizmoMesh        = mf.sharedMesh;
        _gizmoMeshScale   = _fusePrefabs[0].transform.localScale;   // prefab root world scale
        _gizmoChildOffset = mf.transform.localPosition;              // child offset in root-local space

        mesh             = _gizmoMesh;
        meshScale        = _gizmoMeshScale;
        childLocalOffset = _gizmoChildOffset;
        return true;
    }

    private void OnDrawGizmos()
    {
        if (_spawnPoints == null) return;

        bool hasMesh = TryGetFuseGizmoData(
            out Mesh   fuseMesh,
            out Vector3 fuseScale,
            out Vector3 childOffset);

        foreach (Transform point in _spawnPoints)
        {
            if (point == null) continue;

            if (hasMesh)
            {
                // World position of the mesh child when instantiated at this spawn point.
                // Instantiate(prefab, pos, rot) sets root world-pos = pos, world-rot = rot.
                // Child world-pos = root.pos + root.rot * (root.scale ⊙ child.localPos).
                Vector3 meshWorldPos = point.position
                    + point.rotation * Vector3.Scale(fuseScale, childOffset);

                // Filled semi-transparent fuse shape.
                Gizmos.color = new Color(1f, 0.85f, 0f, 0.35f);
                Gizmos.DrawMesh(fuseMesh, meshWorldPos, point.rotation, fuseScale);

                // Wire outline for definition.
                Gizmos.color = new Color(1f, 0.85f, 0f, 0.85f);
                Gizmos.DrawWireMesh(fuseMesh, meshWorldPos, point.rotation, fuseScale);
            }
            else
            {
                // Fallback sphere when prefab mesh isn't available yet.
                Gizmos.color = new Color(1f, 0.85f, 0f, 0.9f);
                Gizmos.DrawSphere(point.position, 0.18f);
                Gizmos.color = new Color(1f, 0.85f, 0f, 0.3f);
                Gizmos.DrawWireSphere(point.position, 0.32f);
            }

            // Vertical drop line — easy to spot from top-down view.
            Gizmos.color = new Color(1f, 0.85f, 0f, 0.5f);
            Gizmos.DrawLine(point.position, point.position + Vector3.down * 0.6f);

#if UNITY_EDITOR
            UnityEditor.Handles.Label(
                point.position + Vector3.up * 0.35f,
                point.name,
                new GUIStyle { normal = { textColor = new Color(1f, 0.9f, 0.2f) }, fontSize = 10 }
            );
#endif
        }

        // Faint lines from FuseSpawner to each point — visualise them as a set.
        Gizmos.color = new Color(1f, 0.85f, 0f, 0.15f);
        foreach (Transform point in _spawnPoints)
        {
            if (point != null)
                Gizmos.DrawLine(transform.position, point.position);
        }
    }
}
