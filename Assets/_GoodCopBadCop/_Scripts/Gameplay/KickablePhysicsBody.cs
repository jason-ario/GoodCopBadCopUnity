using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// Makes a networked physics junk piece (soldier/guard gore, Vlad's torso/head, gut chunks, trash)
/// kickable, with a short-lived, server-authoritative ragdoll that freezes again once it settles.
///
/// Auto-attached by <see cref="JunkItem"/> on every peer (see <see cref="TryAttach"/>) to any junk
/// whose root has a Rigidbody + NetworkRigidbody, so no prefab edits are needed.
///
/// Lifecycle (server):
///   - Idle: every bone body is kinematic (no per-limb simulation cost) and, once settled, the
///     root is kinematic too.
///   - Woken by a kick (<see cref="ApplyKickServer"/>, sent by <see cref="BallKicker"/>) or by the
///     root moving fast on its own (a fresh gore drop, Vlad falling off the roof): limbs go
///     dynamic for at most <see cref="_maxActiveSeconds"/>.
///   - Settles once every body is at rest: limbs + root go kinematic and the final pose is sent.
///
/// Rigs:
///   - Authored ragdolls (child Rigidbodies + joints, e.g. Vlad Torso Networked) are used as-is.
///   - Pieces with collider bones but no child bodies (Soldier/Guard gore) get Rigidbodies +
///     CharacterJoints built on the server the first time they wake (<see cref="_autoBuildRagdoll"/>).
///   - Pieces with no bones (gut chunks) are simply impulsed and re-frozen.
///
/// Networking: the root transform replicates through the existing NetworkTransform +
/// NetworkRigidbody. Limb poses are NOT covered by those, so while a ragdoll is awake the server
/// streams bone-local poses to clients over a named message (~<see cref="_poseSendRate"/> Hz,
/// unreliable) plus one reliable final pose on settle. Clients keep their limbs kinematic and blend
/// toward the received pose; late joiners request the current pose on spawn.
/// </summary>
[DisallowMultipleComponent]
public class KickablePhysicsBody : MonoBehaviour
{
    // ── Tuning ───────────────────────────────────────────────────────────────

    [Header("Kick")]
    [Tooltip("Velocity change (m/s) given to the kicked body at full player run speed.")]
    [SerializeField] private float _kickSpeed = 4.5f;
    [Tooltip("Fraction of the kick also given to the root when a limb is kicked, so the whole piece travels.")]
    [SerializeField, Range(0f, 1f)] private float _rootKickShare = 0.45f;
    [Tooltip("Upward component added to the kick direction before normalizing.")]
    [SerializeField, Min(0f)] private float _kickUpward = 0.35f;
    [Tooltip("Kick strength multiplier at walking speed (lerps to 1 at run speed).")]
    [SerializeField, Range(0f, 1f)] private float _walkKickStrength = 0.55f;
    [Tooltip("Server-side minimum seconds between accepted kicks on this piece.")]
    [SerializeField, Min(0f)] private float _serverKickCooldown = 0.15f;

    [Header("Ragdoll")]
    [Tooltip("Build Rigidbodies + CharacterJoints on collider bones (server-only, first wake) for pieces that have no authored ragdoll.")]
    [SerializeField] private bool _autoBuildRagdoll = true;
    [Tooltip("Mass of each auto-built limb body.")]
    [SerializeField, Min(0.01f)] private float _autoBoneMass = 0.4f;
    [Tooltip("Twist/swing limit (degrees) for auto-built joints.")]
    [SerializeField, Range(5f, 90f)] private float _autoJointLimit = 40f;
    [Tooltip("Wake the ragdoll automatically when the root moves faster than this on its own (fresh drops, falls).")]
    [SerializeField, Min(0f)] private float _wakeSpeed = 1.5f;

    [Header("Settle")]
    [Tooltip("Bodies count as at rest below this linear speed (m/s).")]
    [SerializeField, Min(0f)] private float _restLinearSpeed = 0.15f;
    [Tooltip("Bodies count as at rest below this angular speed (rad/s).")]
    [SerializeField, Min(0f)] private float _restAngularSpeed = 1.2f;
    [Tooltip("Seconds everything must stay at rest before it freezes.")]
    [SerializeField, Min(0f)] private float _settleHoldSeconds = 0.35f;
    [Tooltip("Minimum awake time before a freeze is allowed.")]
    [SerializeField, Min(0f)] private float _minActiveSeconds = 0.6f;
    [Tooltip("Hard cap on awake time — freezes wherever it is after this, so nothing simulates forever.")]
    [SerializeField, Min(0.1f)] private float _maxActiveSeconds = 5f;

    [Header("Pose Sync")]
    [Tooltip("Limb pose messages per second sent to clients while the ragdoll is awake.")]
    [SerializeField, Range(5f, 30f)] private float _poseSendRate = 15f;
    [Tooltip("Client-side blend rate toward the received limb pose.")]
    [SerializeField, Min(1f)] private float _clientPoseBlendRate = 18f;

    // ── Static registry (cheap early-out for BallKicker's probe) ─────────────

    private static int s_activeInstances;

    /// <summary>Number of enabled kickable bodies in the world.</summary>
    public static int ActiveCount => s_activeInstances;

    // ── State ────────────────────────────────────────────────────────────────

    private NetworkObject _netObj;
    private Rigidbody _rootRb;
    private Collider[] _allColliders;

    /// <summary>Bones whose local pose is synced; identical order on every peer (computed in Awake).</summary>
    private Transform[] _poseBones = System.Array.Empty<Transform>();
    private bool _poseBonesNeedAutoRig;
    private readonly List<Rigidbody> _boneBodies = new();
    private bool _rigBuilt;
    private bool HasRig => _poseBones.Length > 0;

    private bool _ragdollActive;
    private float _activeTime;
    private float _restTime;
    private float _rootAwakeTime;
    private float _lastServerKickTime = float.MinValue;
    private float _poseSendTimer;

    private Vector3[] _targetPos;
    private Quaternion[] _targetRot;
    private bool _hasClientTarget;
    private float _clientBlendRemaining;
    private bool _requestedInitialPose;
    private Animator[] _animators;

    /// <summary>Owner-client time of this player's last kick request on this body (BallKicker cooldown).</summary>
    [System.NonSerialized] public float LastLocalKickTime = float.MinValue;

    private bool IsServerRole => _netObj != null && _netObj.IsSpawned && _netObj.NetworkManager != null && _netObj.NetworkManager.IsServer;
    private bool IsClientOnlyRole => _netObj != null && _netObj.IsSpawned && _netObj.NetworkManager != null && !_netObj.NetworkManager.IsServer;

    // ── Attach ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds a kickable body to a standalone physics junk piece. Called from <see cref="JunkItem"/>'s
    /// Awake on every peer, so the component exists identically everywhere without prefab edits.
    /// Skips suspect bodies, mutants and anything character-driven.
    /// </summary>
    public static void TryAttach(Component junkRoot)
    {
        GameObject go = junkRoot.gameObject;
        if (go.GetComponent<KickablePhysicsBody>() != null) return;
        if (go.GetComponent<Rigidbody>() == null) return;
        if (go.GetComponent<NetworkRigidbody>() == null) return;
        if (go.GetComponent<CharacterController>() != null) return;
        if (go.GetComponent<UnityEngine.AI.NavMeshAgent>() != null) return;
        if (go.GetComponent<SuspectCharacter>() != null) return;
        if (go.GetComponentInChildren<RagdollController>(true) != null) return;

        go.AddComponent<KickablePhysicsBody>();
    }

    // ── Unity lifecycle ──────────────────────────────────────────────────────

    private void Awake()
    {
        _netObj = GetComponent<NetworkObject>();
        _rootRb = GetComponent<Rigidbody>();
        _allColliders = GetComponentsInChildren<Collider>(true);
        _animators = GetComponentsInChildren<Animator>(true);

        CollectPoseBones();

        // Authored limb bodies start (and stay, on clients) kinematic — only the server simulates
        // them, and only while awake. This is also what stops every peer from running its own
        // divergent ragdoll forever.
        foreach (Rigidbody rb in _boneBodies)
        {
            if (rb == null) continue;
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
        }

        _targetPos = new Vector3[_poseBones.Length];
        _targetRot = new Quaternion[_poseBones.Length];

        Messaging.EnsureRegistered();
    }

    private void OnEnable()  => s_activeInstances++;
    private void OnDisable() => s_activeInstances = Mathf.Max(0, s_activeInstances - 1);

    private void Update()
    {
        Messaging.EnsureRegistered();

        if (!_requestedInitialPose && HasRig && IsClientOnlyRole)
        {
            _requestedInitialPose = true;
            Messaging.RequestPose(_netObj);
        }
    }

    private void FixedUpdate()
    {
        if (!IsServerRole || _rootRb == null) return;

        float dt = Time.fixedDeltaTime;

        if (_ragdollActive)
        {
            _activeTime += dt;
            _restTime = AllBodiesAtRest(includeBones: true) ? _restTime + dt : 0f;

            if ((_activeTime >= _minActiveSeconds && _restTime >= _settleHoldSeconds) || _activeTime >= _maxActiveSeconds)
                SettleServer();
            return;
        }

        if (_rootRb.isKinematic)
        {
            _rootAwakeTime = 0f;
            return;
        }

        // Root is simulating on its own (fresh spawn drop, cutscene drop, root-only kick).
        if (HasRig && _wakeSpeed > 0f && _rootRb.linearVelocity.sqrMagnitude > _wakeSpeed * _wakeSpeed && AnyColliderEnabled())
        {
            ActivateRagdollServer();
            return;
        }

        _rootAwakeTime += dt;
        _restTime = AllBodiesAtRest(includeBones: false) ? _restTime + dt : 0f;

        if ((_rootAwakeTime >= _minActiveSeconds && _restTime >= _settleHoldSeconds) || _rootAwakeTime >= _maxActiveSeconds * 2f)
            SettleServer();
    }

    private void LateUpdate()
    {
        if (IsServerRole)
        {
            if (!_ragdollActive || !HasRig) return;

            _poseSendTimer += Time.deltaTime;
            if (_poseSendTimer >= 1f / _poseSendRate)
            {
                _poseSendTimer = 0f;
                Messaging.BroadcastPose(this, isFinal: false);
            }
            return;
        }

        if (!_hasClientTarget) return;

        float t = 1f - Mathf.Exp(-_clientPoseBlendRate * Time.deltaTime);
        for (int i = 0; i < _poseBones.Length; i++)
        {
            Transform bone = _poseBones[i];
            if (bone == null) continue;
            bone.localPosition = Vector3.Lerp(bone.localPosition, _targetPos[i], t);
            bone.localRotation = Quaternion.Slerp(bone.localRotation, _targetRot[i], t);
        }

        if (_clientBlendRemaining > 0f)
        {
            _clientBlendRemaining -= Time.deltaTime;
            if (_clientBlendRemaining <= 0f)
            {
                SnapToTarget();
                _hasClientTarget = false;
            }
        }
    }

    // ── Server: kick / wake / settle ─────────────────────────────────────────

    /// <summary>
    /// Server-only. Applies a kick coming from <see cref="BallKicker"/>.
    /// </summary>
    /// <param name="direction">Kick direction (world); flattened and lifted by <see cref="_kickUpward"/>.</param>
    /// <param name="point">World point nearest the kicker's foot; picks which limb takes the hit.</param>
    /// <param name="playerSpeed">Kicker's horizontal speed, scales strength between walk and run.</param>
    public void ApplyKickServer(Vector3 direction, Vector3 point, float playerSpeed)
    {
        if (!IsServerRole || _rootRb == null) return;
        if (Time.time - _lastServerKickTime < _serverKickCooldown) return;
        if (!AnyColliderEnabled()) return; // held / hidden (e.g. Vlad during the cutscene)

        _lastServerKickTime = Time.time;

        direction.y = 0f;
        if (direction.sqrMagnitude < 0.0001f) direction = transform.forward;
        direction = (direction.normalized + Vector3.up * _kickUpward).normalized;

        float strength = Mathf.Lerp(_walkKickStrength, 1f, Mathf.InverseLerp(1.2f, 6f, playerSpeed));
        Vector3 deltaV = direction * (_kickSpeed * strength);

        if (HasRig)
        {
            ActivateRagdollServer();

            Rigidbody hitBody = NearestBody(point);
            hitBody.AddForce(deltaV, ForceMode.VelocityChange);
            if (hitBody != _rootRb)
                _rootRb.AddForce(deltaV * _rootKickShare, ForceMode.VelocityChange);
        }
        else
        {
            _rootRb.isKinematic = false;
            _rootRb.WakeUp();
            _rootRb.AddForce(deltaV, ForceMode.VelocityChange);
            _rootAwakeTime = 0f;
            _restTime = 0f;
        }
    }

    private void ActivateRagdollServer()
    {
        EnsureServerRig();

        if (!_ragdollActive)
        {
            foreach (Animator animator in _animators)
                if (animator != null) animator.enabled = false;

            _rootRb.isKinematic = false;
            foreach (Rigidbody rb in _boneBodies)
            {
                if (rb == null) continue;
                rb.isKinematic = false;
                rb.WakeUp();
            }
            _rootRb.WakeUp();
        }

        _ragdollActive = true;
        _activeTime = 0f;
        _restTime = 0f;
        _poseSendTimer = 1f; // send one pose right away
    }

    private void SettleServer()
    {
        foreach (Rigidbody rb in _boneBodies)
        {
            if (rb == null || rb.isKinematic) continue;
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
            rb.isKinematic = true;
        }

        if (!_rootRb.isKinematic)
        {
            _rootRb.linearVelocity = Vector3.zero;
            _rootRb.angularVelocity = Vector3.zero;
            _rootRb.isKinematic = true;
        }

        bool wasRagdoll = _ragdollActive;
        _ragdollActive = false;
        _rootAwakeTime = 0f;
        _restTime = 0f;

        if (wasRagdoll && HasRig)
            Messaging.BroadcastPose(this, isFinal: true);
    }

    private bool AllBodiesAtRest(bool includeBones)
    {
        float lin = _restLinearSpeed * _restLinearSpeed;
        float ang = _restAngularSpeed * _restAngularSpeed;

        if (!_rootRb.isKinematic &&
            (_rootRb.linearVelocity.sqrMagnitude > lin || _rootRb.angularVelocity.sqrMagnitude > ang))
            return false;

        if (!includeBones) return true;

        foreach (Rigidbody rb in _boneBodies)
        {
            if (rb == null || rb.isKinematic) continue;
            if (rb.linearVelocity.sqrMagnitude > lin || rb.angularVelocity.sqrMagnitude > ang)
                return false;
        }
        return true;
    }

    private Rigidbody NearestBody(Vector3 point)
    {
        Rigidbody best = _rootRb;
        float bestDist = (_rootRb.worldCenterOfMass - point).sqrMagnitude;

        foreach (Rigidbody rb in _boneBodies)
        {
            if (rb == null) continue;
            float d = (rb.worldCenterOfMass - point).sqrMagnitude;
            if (d < bestDist)
            {
                bestDist = d;
                best = rb;
            }
        }
        return best;
    }

    private bool AnyColliderEnabled()
    {
        foreach (Collider c in _allColliders)
            if (c != null && c.enabled && !c.isTrigger && c.gameObject.activeInHierarchy)
                return true;
        return false;
    }

    // ── Rig discovery / auto-build ───────────────────────────────────────────

    /// <summary>
    /// Deterministic on every peer (runs in Awake, before any server-only auto-rigging), so the
    /// pose message's bone order always matches.
    /// </summary>
    private void CollectPoseBones()
    {
        var bones = new List<Transform>();

        foreach (Rigidbody rb in GetComponentsInChildren<Rigidbody>(true))
        {
            if (rb == _rootRb) continue;
            bones.Add(rb.transform);
            _boneBodies.Add(rb);
        }

        if (bones.Count == 0 && _autoBuildRagdoll)
        {
            foreach (Transform t in GetComponentsInChildren<Transform>(true))
            {
                if (t == transform) continue;
                if (IsAutoRigCandidate(t))
                    bones.Add(t);
            }
            _poseBonesNeedAutoRig = bones.Count > 0;
        }

        _rigBuilt = !_poseBonesNeedAutoRig;
        _poseBones = bones.ToArray();
    }

    private static bool IsAutoRigCandidate(Transform t)
    {
        foreach (Collider c in t.GetComponents<Collider>())
        {
            if (c.isTrigger) continue;
            if (c is MeshCollider mc && !mc.convex) continue;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Server-only, first wake. Gives each collider bone a Rigidbody + CharacterJoint connected to
    /// its nearest ancestor body (ultimately the root). Clients never build this — they only
    /// receive poses.
    /// </summary>
    private void EnsureServerRig()
    {
        if (_rigBuilt) return;
        _rigBuilt = true;

        var created = new List<Rigidbody>();

        // Preorder hierarchy traversal → parents get bodies before their children look them up.
        foreach (Transform bone in _poseBones)
        {
            if (bone == null) continue;
            Rigidbody rb = bone.gameObject.AddComponent<Rigidbody>();
            rb.mass = _autoBoneMass;
            rb.linearDamping = 0.4f;
            rb.angularDamping = 2f;
            rb.solverIterations = 12;
            rb.interpolation = RigidbodyInterpolation.None;
            rb.collisionDetectionMode = CollisionDetectionMode.Discrete;
            rb.isKinematic = true;
            created.Add(rb);
        }

        foreach (Rigidbody rb in created)
        {
            Rigidbody parentBody = FindAncestorBody(rb.transform);
            if (parentBody == null) continue;

            CharacterJoint joint = rb.gameObject.AddComponent<CharacterJoint>();
            joint.connectedBody = parentBody;
            joint.autoConfigureConnectedAnchor = true;
            joint.anchor = Vector3.zero;

            Vector3 twistAxis = GetBoneAxis(rb.transform);
            joint.axis = twistAxis;
            joint.swingAxis = Mathf.Abs(Vector3.Dot(twistAxis, Vector3.right)) > 0.9f ? Vector3.forward : Vector3.right;

            joint.lowTwistLimit  = new SoftJointLimit { limit = -_autoJointLimit };
            joint.highTwistLimit = new SoftJointLimit { limit = _autoJointLimit };
            joint.swing1Limit    = new SoftJointLimit { limit = _autoJointLimit };
            joint.swing2Limit    = new SoftJointLimit { limit = _autoJointLimit };
            joint.enableProjection = true;
            joint.enablePreprocessing = false;
        }

        _boneBodies.AddRange(created);

        // The limbs overlap the root's own collider and each other at rest; without this the
        // first wake would explode them apart.
        var ragdollColliders = new List<Collider>();
        foreach (Collider c in _allColliders)
            if (c != null && !c.isTrigger) ragdollColliders.Add(c);

        for (int i = 0; i < ragdollColliders.Count; i++)
            for (int j = i + 1; j < ragdollColliders.Count; j++)
                Physics.IgnoreCollision(ragdollColliders[i], ragdollColliders[j], true);
    }

    private Rigidbody FindAncestorBody(Transform bone)
    {
        Transform t = bone.parent;
        while (t != null)
        {
            if (t.TryGetComponent(out Rigidbody rb)) return rb;
            if (t == transform) break;
            t = t.parent;
        }
        return _rootRb;
    }

    private static Vector3 GetBoneAxis(Transform bone)
    {
        if (bone.TryGetComponent(out CapsuleCollider capsule))
        {
            return capsule.direction switch
            {
                0 => Vector3.right,
                2 => Vector3.forward,
                _ => Vector3.up
            };
        }
        return Vector3.up; // Blender-style bones point along local +Y
    }

    // ── Client pose application ──────────────────────────────────────────────

    private void OnPoseReceived(bool isFinal, bool snap)
    {
        foreach (Animator animator in _animators)
            if (animator != null) animator.enabled = false;

        if (snap)
        {
            SnapToTarget();
            _hasClientTarget = false;
            return;
        }

        _hasClientTarget = true;
        _clientBlendRemaining = isFinal ? 0.5f : 0f;
    }

    private void SnapToTarget()
    {
        for (int i = 0; i < _poseBones.Length; i++)
        {
            Transform bone = _poseBones[i];
            if (bone == null) continue;
            bone.localPosition = _targetPos[i];
            bone.localRotation = _targetRot[i];
        }
    }

    // ── Networking (named messages; this is a plain MonoBehaviour so it can be auto-attached) ─

    private static class Messaging
    {
        private const string PoseMessage = "KickablePhysicsBody.Pose";
        private const string RequestMessage = "KickablePhysicsBody.RequestPose";

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

        public static void BroadcastPose(KickablePhysicsBody body, bool isFinal)
        {
            NetworkManager nm = body._netObj.NetworkManager;
            if (nm == null || nm.CustomMessagingManager == null) return;

            s_targets.Clear();
            foreach (ulong id in nm.ConnectedClientsIds)
                if (id != nm.LocalClientId) s_targets.Add(id);
            if (s_targets.Count == 0) return;

            Send(nm, body, isFinal, snap: false, s_targets);
        }

        private static void SendTo(NetworkManager nm, KickablePhysicsBody body, ulong clientId)
        {
            s_targets.Clear();
            s_targets.Add(clientId);
            Send(nm, body, isFinal: true, snap: true, s_targets);
        }

        private static void Send(NetworkManager nm, KickablePhysicsBody body, bool isFinal, bool snap, List<ulong> targets)
        {
            Transform[] bones = body._poseBones;
            int count = Mathf.Min(bones.Length, ushort.MaxValue);
            int size = sizeof(ulong) + 2 * sizeof(bool) + sizeof(ushort) + count * (3 + 4) * sizeof(float);

            // Streamed poses may be dropped (a newer one follows); the final/snap pose must arrive.
            NetworkDelivery delivery = (isFinal || snap || size > 1100)
                ? NetworkDelivery.ReliableFragmentedSequenced
                : NetworkDelivery.UnreliableSequenced;

            using var writer = new FastBufferWriter(size, Allocator.Temp);
            writer.WriteValueSafe(body._netObj.NetworkObjectId);
            writer.WriteValueSafe(isFinal);
            writer.WriteValueSafe(snap);
            writer.WriteValueSafe((ushort)count);
            for (int i = 0; i < count; i++)
            {
                Transform bone = bones[i];
                writer.WriteValueSafe(bone != null ? bone.localPosition : Vector3.zero);
                writer.WriteValueSafe(bone != null ? bone.localRotation : Quaternion.identity);
            }

            nm.CustomMessagingManager.SendNamedMessage(PoseMessage, targets, writer, delivery);
        }

        private static KickablePhysicsBody Find(NetworkManager nm, ulong networkObjectId)
        {
            if (nm == null || nm.SpawnManager == null) return null;
            if (!nm.SpawnManager.SpawnedObjects.TryGetValue(networkObjectId, out NetworkObject no) || no == null)
                return null;
            return no.GetComponent<KickablePhysicsBody>();
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

            KickablePhysicsBody body = Find(nm, id);
            if (body == null || count != body._poseBones.Length) return;

            for (int i = 0; i < count; i++)
            {
                reader.ReadValueSafe(out Vector3 pos);
                reader.ReadValueSafe(out Quaternion rot);
                body._targetPos[i] = pos;
                body._targetRot[i] = rot;
            }

            body.OnPoseReceived(isFinal, snap);
        }

        private static void OnRequestMessage(ulong senderClientId, FastBufferReader reader)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsServer) return;

            reader.ReadValueSafe(out ulong id);
            KickablePhysicsBody body = Find(nm, id);
            if (body == null || !body.HasRig) return;

            SendTo(nm, body, senderClientId);
        }
    }
}
