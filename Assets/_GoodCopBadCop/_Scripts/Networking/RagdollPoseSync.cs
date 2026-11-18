using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Server-authoritative pose replication for a <see cref="RagdollController"/> corpse (e.g. a killed
/// <see cref="MutantEnemy"/>). Only the server simulates the ragdoll; clients put their copy into
/// network-proxy mode (bodies kinematic, colliders on — see <see cref="RagdollController.SetNetworkProxy"/>)
/// and follow the server's bone poses, so every peer sees the body land in the same place.
///
/// Mirrors <see cref="KickablePhysicsBody"/>'s pose sync: while the server ragdoll is live it streams
/// world-space bone poses (~<see cref="PoseSendRate"/> Hz, unreliable), sends one reliable final pose
/// once <see cref="RagdollController.IsFrozen"/>, and answers late-joiner snapshot requests.
/// Plain MonoBehaviour + named messages so it can be added at runtime (<see cref="Attach"/>).
/// </summary>
[DisallowMultipleComponent]
public class RagdollPoseSync : MonoBehaviour
{
    private const float PoseSendRate = 15f;
    private const float ClientBlendRate = 18f;
    private const float ClientStreamBlendSeconds = 0.75f;
    private const float ClientFinalBlendSeconds = 0.5f;
    private const float MaxServerStreamSeconds = 12f;

    private static readonly Dictionary<ulong, RagdollPoseSync> s_byNetworkId = new();

    private NetworkObject _netObj;
    private RagdollController _ragdoll;
    private Transform[] _bones = System.Array.Empty<Transform>();
    private Vector3[] _targetPos;
    private Quaternion[] _targetRot;

    private bool _isServer;
    private bool _finalSent;
    private float _serverStreamTime;
    private float _sendTimer;

    private bool _hasClientTarget;
    private float _clientBlendRemaining;
    private bool _requestedSnapshot;

    // ── Setup ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds (or returns) the sync component for <paramref name="netObj"/> and binds it to
    /// <paramref name="ragdoll"/>. Call on every peer once the corpse ragdoll is activated; on clients
    /// this also switches the ragdoll to network-proxy mode and requests the current pose.
    /// </summary>
    public static RagdollPoseSync Attach(NetworkObject netObj, RagdollController ragdoll)
    {
        if (netObj == null || ragdoll == null || !netObj.IsSpawned) return null;

        if (!netObj.TryGetComponent(out RagdollPoseSync sync))
            sync = netObj.gameObject.AddComponent<RagdollPoseSync>();

        sync.Bind(netObj, ragdoll);
        return sync;
    }

    private void Bind(NetworkObject netObj, RagdollController ragdoll)
    {
        Messaging.EnsureRegistered();

        bool rebind = _ragdoll != ragdoll;
        _netObj = netObj;
        _ragdoll = ragdoll;
        _isServer = netObj.NetworkManager != null && netObj.NetworkManager.IsServer;
        s_byNetworkId[netObj.NetworkObjectId] = this;

        if (rebind)
            CollectBones();

        if (_isServer)
        {
            _finalSent = false;
            _serverStreamTime = 0f;
            _sendTimer = 1f / PoseSendRate; // send one pose right away
        }
        else
        {
            _ragdoll.SetNetworkProxy(true);
            if (!_requestedSnapshot)
            {
                _requestedSnapshot = true;
                Messaging.RequestPose(_netObj);
            }
        }

        enabled = true;
    }

    /// <summary>
    /// Ragdoll bones in parent-before-child order (identical on every peer: same prefab, same
    /// serialized/auto-resolved RagdollController array), so world poses can be applied in sequence.
    /// </summary>
    private void CollectBones()
    {
        var list = new List<Transform>();
        foreach (Rigidbody rb in _ragdoll.RagdollRigidbodies)
            if (rb != null) list.Add(rb.transform);

        // Stable sort by hierarchy depth.
        var depth = new Dictionary<Transform, int>(list.Count);
        foreach (Transform t in list)
        {
            int d = 0;
            for (Transform p = t.parent; p != null; p = p.parent) d++;
            depth[t] = d;
        }
        var indexed = new List<(Transform t, int i)>(list.Count);
        for (int i = 0; i < list.Count; i++) indexed.Add((list[i], i));
        indexed.Sort((a, b) => depth[a.t] != depth[b.t] ? depth[a.t].CompareTo(depth[b.t]) : a.i.CompareTo(b.i));

        _bones = new Transform[indexed.Count];
        for (int i = 0; i < indexed.Count; i++) _bones[i] = indexed[i].t;
        _targetPos = new Vector3[_bones.Length];
        _targetRot = new Quaternion[_bones.Length];
    }

    private void OnDestroy()
    {
        if (_netObj != null && s_byNetworkId.TryGetValue(_netObj.NetworkObjectId, out RagdollPoseSync s) && s == this)
            s_byNetworkId.Remove(_netObj.NetworkObjectId);
    }

    // ── Update ───────────────────────────────────────────────────────────────

    private void LateUpdate()
    {
        if (_ragdoll == null || _netObj == null || !_netObj.IsSpawned) return;

        if (_isServer)
        {
            if (_finalSent || !_ragdoll.IsRagdollActive) return;

            // Final pose once the server ragdoll freezes — or after a hard cap, for rigs with
            // RagdollController.freezeWhenSettled off that never report IsFrozen.
            _serverStreamTime += Time.deltaTime;
            if (_ragdoll.IsFrozen || _serverStreamTime >= MaxServerStreamSeconds)
            {
                _finalSent = true;
                Messaging.BroadcastPose(this, isFinal: true);
                return;
            }

            _sendTimer += Time.deltaTime;
            if (_sendTimer >= 1f / PoseSendRate)
            {
                _sendTimer = 0f;
                Messaging.BroadcastPose(this, isFinal: false);
            }
            return;
        }

        if (!_hasClientTarget) return;

        float t = 1f - Mathf.Exp(-ClientBlendRate * Time.deltaTime);
        for (int i = 0; i < _bones.Length; i++)
        {
            Transform bone = _bones[i];
            if (bone == null) continue;
            bone.SetPositionAndRotation(
                Vector3.Lerp(bone.position, _targetPos[i], t),
                Quaternion.Slerp(bone.rotation, _targetRot[i], t));
        }

        _clientBlendRemaining -= Time.deltaTime;
        if (_clientBlendRemaining <= 0f)
        {
            SnapToTarget();
            _hasClientTarget = false;
        }
    }

    private void OnPoseReceived(bool isFinal, bool snap)
    {
        // A pose can beat the corpse-state RPC/NetworkVariable; never let the client simulate.
        if (!_ragdoll.IsNetworkProxy) _ragdoll.SetNetworkProxy(true);

        if (snap)
        {
            SnapToTarget();
            _hasClientTarget = false;
            return;
        }

        _hasClientTarget = true;
        _clientBlendRemaining = isFinal ? ClientFinalBlendSeconds : ClientStreamBlendSeconds;
    }

    private void SnapToTarget()
    {
        for (int i = 0; i < _bones.Length; i++)
            if (_bones[i] != null) _bones[i].SetPositionAndRotation(_targetPos[i], _targetRot[i]);
    }

    // ── Networking ───────────────────────────────────────────────────────────

    private static class Messaging
    {
        private const string PoseMessage = "RagdollPoseSync.Pose";
        private const string RequestMessage = "RagdollPoseSync.RequestPose";

        private static CustomMessagingManager s_registeredOn;
        private static readonly List<ulong> s_targets = new();

        public static void EnsureRegistered()
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.CustomMessagingManager == null) return;
            if (s_registeredOn == nm.CustomMessagingManager) return;

            s_registeredOn = nm.CustomMessagingManager;
            s_registeredOn.RegisterNamedMessageHandler(PoseMessage, OnPoseMessage);
            s_registeredOn.RegisterNamedMessageHandler(RequestMessage, OnRequestMessage);
        }

        public static void RequestPose(NetworkObject netObj)
        {
            NetworkManager nm = netObj.NetworkManager;
            if (nm == null || nm.CustomMessagingManager == null) return;
            EnsureRegistered();

            using var writer = new FastBufferWriter(sizeof(ulong), Allocator.Temp);
            writer.WriteValueSafe(netObj.NetworkObjectId);
            nm.CustomMessagingManager.SendNamedMessage(RequestMessage, NetworkManager.ServerClientId, writer, NetworkDelivery.Reliable);
        }

        public static void BroadcastPose(RagdollPoseSync sync, bool isFinal)
        {
            NetworkManager nm = sync._netObj.NetworkManager;
            if (nm == null || nm.CustomMessagingManager == null) return;

            s_targets.Clear();
            foreach (ulong id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId) s_targets.Add(id);
            if (s_targets.Count == 0) return;

            Send(nm, sync, isFinal, snap: false, s_targets);
        }

        private static void Send(NetworkManager nm, RagdollPoseSync sync, bool isFinal, bool snap, List<ulong> targets)
        {
            Transform[] bones = sync._bones;
            int count = Mathf.Min(bones.Length, ushort.MaxValue);
            int size = sizeof(ulong) + 2 * sizeof(bool) + sizeof(ushort) + count * (3 + 4) * sizeof(float);

            NetworkDelivery delivery = (isFinal || snap || size > 1100)
                ? NetworkDelivery.ReliableFragmentedSequenced
                : NetworkDelivery.UnreliableSequenced;

            using var writer = new FastBufferWriter(size, Allocator.Temp);
            writer.WriteValueSafe(sync._netObj.NetworkObjectId);
            writer.WriteValueSafe(isFinal);
            writer.WriteValueSafe(snap);
            writer.WriteValueSafe((ushort)count);
            for (int i = 0; i < count; i++)
            {
                Transform bone = bones[i];
                writer.WriteValueSafe(bone != null ? bone.position : Vector3.zero);
                writer.WriteValueSafe(bone != null ? bone.rotation : Quaternion.identity);
            }

            nm.CustomMessagingManager.SendNamedMessage(PoseMessage, targets, writer, delivery);
        }

        private static void OnPoseMessage(ulong senderClientId, FastBufferReader reader)
        {
            if (senderClientId != NetworkManager.ServerClientId) return;

            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || nm.IsServer) return;

            reader.ReadValueSafe(out ulong id);
            reader.ReadValueSafe(out bool isFinal);
            reader.ReadValueSafe(out bool snap);
            reader.ReadValueSafe(out ushort count);

            if (!s_byNetworkId.TryGetValue(id, out RagdollPoseSync sync) || sync == null || sync._ragdoll == null)
                return; // not bound yet — the client requests a snapshot as soon as it binds
            if (count != sync._bones.Length) return;

            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out Vector3 pos);
                reader.ReadValueSafe(out Quaternion rot);
                sync._targetPos[i] = pos;
                sync._targetRot[i] = rot;
            }

            sync.OnPoseReceived(isFinal, snap);
        }

        private static void OnRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            reader.ReadValueSafe(out ulong id);
            if (!s_byNetworkId.TryGetValue(id, out RagdollPoseSync sync) || sync == null || sync._ragdoll == null)
                return;

            s_targets.Clear();
            s_targets.Add(senderClientId);
            Send(nm, sync, isFinal: true, snap: true, s_targets);
        }
    }
}
