using System.Collections.Generic;
using GoodCopBadCop.Effects;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Debug-only "fake multiplayer" helper. Spawns server-owned copies of the player prefab that
/// act like a teammate's body on the host, so co-op-only flows (teammate death, ragdoll, corpse
/// junk highlight, resurrection) can be tested solo.
/// <para>
/// The dummy is spawned with a plain <see cref="NetworkObject.Spawn"/> instead of
/// <c>SpawnAsPlayerObject</c>, which makes it the same kind of object as a retained corpse after
/// a revive. Player camera, input, HUD and local-player setup are gated on
/// <c>IsLocalPlayer</c> (false here), so it renders as a remote teammate and never takes over
/// the local player. It isn't a connected client, so <see cref="ReviveManager"/>, spawn and
/// player-count logic ignore it. It doesn't move (movement only runs on the local player).
/// </para>
/// Host/server only. Driven by <see cref="DebugConsole"/> hotkeys and the F12 cheat console.
/// </summary>
public static class FakeTeammateTester
{
    private const float SpawnDistance = 2.5f;

    private static readonly List<NetworkObject> s_fakes = new();

    /// <summary>Spawns a fake teammate in front of the local player, facing them.</summary>
    public static void Spawn()
    {
        if (!CanRun()) return;

        GameObject prefab = PlayerSpawner.Instance != null ? PlayerSpawner.Instance.PlayerPrefab : null;
        if (prefab == null)
        {
            Debug.LogWarning("[FakeTeammate] PlayerSpawner or its player prefab is missing.");
            return;
        }

        PlayerInstance local = PlayerInstance.Instance;
        if (local == null)
        {
            Debug.LogWarning("[FakeTeammate] No local player yet — start the game first.");
            return;
        }

        Transform lt = local.transform;
        Vector3 forward = Vector3.ProjectOnPlane(lt.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f) forward = Vector3.forward;

        Vector3 position = lt.position + forward * SpawnDistance;
        Quaternion rotation = Quaternion.LookRotation(-forward, Vector3.up);

        GameObject go = Object.Instantiate(prefab, position, rotation);
        go.name = $"FakeTeammate (Debug) {s_fakes.Count + 1}";

        NetworkObject netObj = go.GetComponent<NetworkObject>();
        if (netObj == null)
        {
            Debug.LogError("[FakeTeammate] Player prefab has no NetworkObject.");
            Object.Destroy(go);
            return;
        }

        netObj.Spawn(destroyWithScene: true);
        s_fakes.Add(netObj);

        Debug.Log($"[FakeTeammate] Spawned '{go.name}' at {position}. Kill it with L, remove all with Shift+P.");
    }

    /// <summary>Kills the living fake teammate closest to the local player.</summary>
    public static void KillNearest()
    {
        if (!CanRun()) return;
        Prune();

        Vector3 origin = PlayerInstance.Instance != null ? PlayerInstance.Instance.transform.position : Vector3.zero;
        PlayerHealth best = null;
        float bestSqr = float.MaxValue;

        foreach (NetworkObject fake in s_fakes)
        {
            if (!fake.TryGetComponent(out PlayerHealth health) || health.IsDead) continue;

            float sqr = (fake.transform.position - origin).sqrMagnitude;
            if (sqr < bestSqr)
            {
                bestSqr = sqr;
                best = health;
            }
        }

        if (best == null)
        {
            Debug.LogWarning("[FakeTeammate] No living fake teammate. Spawn one with P.");
            return;
        }

        best.TakeDamage(999f, EffectKeys.PlayerDeath);
        Debug.Log($"[FakeTeammate] Killed '{best.name}'.");
    }

    /// <summary>Despawns every fake teammate, dead or alive (including bodies still lying around).</summary>
    public static void DespawnAll()
    {
        if (!CanRun()) return;
        Prune();

        int count = 0;
        foreach (NetworkObject fake in s_fakes)
        {
            if (fake.IsSpawned)
            {
                fake.Despawn(true);
                count++;
            }
        }

        s_fakes.Clear();
        Debug.Log($"[FakeTeammate] Removed {count} fake teammate(s).");
    }

    private static bool CanRun()
    {
        NetworkManager nm = NetworkManager.Singleton;
        if (nm == null || !nm.IsListening)
        {
            Debug.LogWarning("[FakeTeammate] No network session running — start the game first.");
            return false;
        }

        if (!nm.IsServer)
        {
            Debug.LogWarning("[FakeTeammate] Host only.");
            return false;
        }

        return true;
    }

    // Bodies can be despawned by the game (bagged/burned corpse, resurrected mutant removed).
    private static void Prune() => s_fakes.RemoveAll(f => f == null || !f.IsSpawned);
}
