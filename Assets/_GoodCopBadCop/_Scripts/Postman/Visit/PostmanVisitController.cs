using System;
using System.Collections;
using GoodCopBadCop.Visuals;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;

namespace GoodCopBadCop.Postman
{
    /// <summary>Server-side phase of the postman's daily visit.</summary>
    public enum PostmanVisitState : byte
    {
        Hidden,
        Arriving,
        Waiting,
        Leaving,
        /// <summary>Turned into a full mutant; MutantEnemy owns him from here on.</summary>
        Mutant
    }

    /// <summary>One day's postman talk: the scripted conversation, then the line he repeats after it.</summary>
    [Serializable]
    public class PostmanDayDialogue
    {
        public int day = 1;

        [Tooltip("Plays the first time a player talks to him this day (intro-style, line after line).")]
        public ScriptedDialogue conversation;

        [Tooltip("Plays on every later talk this day, once the conversation is finished.")]
        public ScriptedDialogue farewell;
    }
    /// <summary>
    /// Server-authoritative daily visit of Givi the postman (a scene-placed <see cref="SuspectCharacter"/>).
    ///
    /// Each day, shortly after the day starts (Day 1: when the last suspect is processed) he:
    /// fades in at <see cref="_spawnPos"/> → walks to <see cref="_placingMailPos"/> → turns to its
    /// rotation → plays "Give" and drops today's newspaper in the mailbox
    /// (<see cref="DailyNewspaperSpawnManager.DeliverTodaysNewspaperServer"/>) → turns toward
    /// <see cref="_finalPos"/> → walks there → turns to its rotation → waits. Talking to him plays today's scripted conversation
    /// (<see cref="PostmanDayDialogue.conversation"/>, via <see cref="ScriptedDialogueRunner"/>); once
    /// it has been finished, talking again plays the short <see cref="PostmanDayDialogue.farewell"/>.
    /// Once he has waited <see cref="_stayDuration"/> seconds AND today's conversation is finished, he
    /// walks back to the spawn point and fades out.
    /// Every new day (<see cref="ShiftManager.OnDayStart"/>) resets him to hidden.
    ///
    /// Look: each day shows one child of the character's "Base Version" (Day 1 = first child, Day 2 =
    /// second, ...; later days keep the last one), replicated through <see cref="_modelVariant"/>.
    /// On <see cref="_mutateDay"/> he shows the Mutated Version all day while still behaving normally.
    ///
    /// Mutation: on <see cref="_mutateDay"/>, finishing his conversation turns him into a full mutant
    /// (<see cref="SuspectCharacter.ActivateAsLegacyMutant"/>) that hunts the player who talked to him,
    /// exactly like a full mutant that has climbed through the booth window. From then on he is
    /// registered in <see cref="NpcMutantPool"/> so <see cref="MutantSpawner"/>s can spawn
    /// <see cref="_worldMutantPrefab"/> as him, and he never visits again.
    ///
    /// Networking: movement replicates through the character's NetworkTransform, animation through
    /// <see cref="SuspectCharacter.SetLocomotionState"/>/<see cref="SuspectCharacter.FireAnimatorTrigger"/>,
    /// visibility through <see cref="_visible"/> (late joiners get the current state instantly) and
    /// interaction through <see cref="SuspectCharacter.SetInteractionLockedServer"/>.
    /// The SuspectCharacter must have "Hidden Until Revealed" ticked so every peer starts hidden.
    /// </summary>
    [RequireComponent(typeof(SuspectCharacter))]
    public class PostmanVisitController : NetworkBehaviour, ISuspectTalkOverride
    {
        [Header("References")]
        [SerializeField] private SuspectCharacter _character;
        [SerializeField] private CharacterDitherFade _fade;
        [SerializeField] private DailyNewspaperSpawnManager _newspaperManager;

        [Header("Route")]
        [SerializeField] private Transform _spawnPos;
        [SerializeField] private Transform _placingMailPos;
        [SerializeField] private Transform _finalPos;

        [Header("Schedule")]
        [Tooltip("Seconds after the day starts before he arrives (every day except the end-of-shift day).")]
        [SerializeField, Min(0f)] private float _dayStartArrivalDelay = 60f;

        [Tooltip("On this day he arrives when the last suspect is processed instead of at day start.")]
        [SerializeField] private int _endOfShiftArrivalDay = 1;

        [Tooltip("Seconds he waits at the final position before he may leave. He only leaves once a " +
                 "player has also finished today's conversation with him.")]
        [SerializeField] private float _stayDuration = 300f;

        [Header("Presentation")]
        [SerializeField] private float _fadeInDuration = 1.5f;
        [SerializeField] private float _fadeOutDuration = 1.5f;

        [Tooltip("Turn speed (degrees per second) for the in-place rotations between route legs.")]
        [SerializeField] private float _turnSpeed = 240f;

        [SerializeField] private string _giveTrigger = "Give";

        [Tooltip("Seconds into the Give animation when the newspaper appears in the mailbox.")]
        [SerializeField] private float _giveDropDelay = 1f;

        [Tooltip("Seconds after the drop before he turns away from the mailbox.")]
        [SerializeField] private float _giveRecoverTime = 1.2f;

        [Header("Notification")]
        [SerializeField] private string _arrivalNotification = "The postman is arriving with today's mail.";

        [Header("Dialogue")]
        [Tooltip("Per-day scripted dialogue. The entry matching ShiftManager.CurrentDay is used (else the " +
                 "highest day below it, else the first entry).")]
        [SerializeField] private PostmanDayDialogue[] _dayDialogues;

        [Header("Mutation")]
        [Tooltip("Finishing his conversation on this day turns him into a full mutant. He never visits after it.")]
        [SerializeField] private int _mutateDay = 4;

        [Tooltip("Seconds between the end of the conversation and the transformation.")]
        [SerializeField] private float _mutateDelay = 0.5f;

        [Tooltip("Id in NpcMutantPool (also used for the persistent killed flag).")]
        [SerializeField] private string _mutantPoolId = "Givi";

        [Tooltip("Networked prefab MutantSpawners spawn as his full-mutant form once he has turned " +
                 "(Suspect_Postman Variant). Must be in the NetworkPrefabs list.")]
        [SerializeField] private GameObject _worldMutantPrefab;

        private const string ArrivalNotificationKey = "PostmanArrival";

        private readonly NetworkVariable<bool> _visible = new NetworkVariable<bool>(false);

        // Index of the Base Version child shown today (server writes, everyone applies).
        private readonly NetworkVariable<int> _modelVariant = new NetworkVariable<int>(0);
        private PostmanVisitState _state = PostmanVisitState.Hidden;

        /// <summary>Server-only. Current visit phase.</summary>
        public PostmanVisitState State => _state;

        // Server-only per-day bookkeeping.
        private bool _visitedToday;
        private bool _conversationDoneToday;
        private bool _dialogueRunning;
        private int _visitGeneration;
        private Coroutine _visitRoutine;
        private bool _subscribed;
        private NetworkTransform _networkTransform;

        private void Reset()
        {
            _character = GetComponent<SuspectCharacter>();
            _fade = GetComponent<CharacterDitherFade>();
        }

        private void Awake()
        {
            if (_character == null) _character = GetComponent<SuspectCharacter>();
            if (_fade == null) _fade = GetComponent<CharacterDitherFade>();
            _networkTransform = GetComponent<NetworkTransform>();
        }

        // ── Lifecycle ────────────────────────────────────────────────────────────

        public override void OnNetworkSpawn()
        {
            base.OnNetworkSpawn();

            _visible.OnValueChanged += HandleVisibleChanged;
            ApplyVisibilityInstant(_visible.Value);
            _modelVariant.OnValueChanged += HandleModelVariantChanged;
            ApplyModelVariant(_modelVariant.Value);

            if (!IsServer) return;

            NpcMutantPool.Register(_mutantPoolId, _worldMutantPrefab, _mutateDay);
            _character.SetInteractionLockedServer(true);
            StartCoroutine(SubscribeWhenReady());
        }

        public override void OnNetworkDespawn()
        {
            base.OnNetworkDespawn();
            _visible.OnValueChanged -= HandleVisibleChanged;
            _modelVariant.OnValueChanged -= HandleModelVariantChanged;
            Unsubscribe();
        }

        public override void OnDestroy()
        {
            Unsubscribe();
            base.OnDestroy();
        }

        private IEnumerator SubscribeWhenReady()
        {
            yield return new WaitUntil(() => ShiftManager.Instance != null);

            ShiftManager.Instance.OnDayStart += HandleDayStart;
            ShiftManager.OnLastSuspectProcessed += HandleLastSuspectProcessed;
            _subscribed = true;

            _modelVariant.Value = ModelVariantForDay(CurrentDay);
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _subscribed = false;

            if (ShiftManager.Instance != null)
                ShiftManager.Instance.OnDayStart -= HandleDayStart;
            ShiftManager.OnLastSuspectProcessed -= HandleLastSuspectProcessed;
        }

        // ── Schedule (server) ────────────────────────────────────────────────────

        private int CurrentDay => ShiftManager.Instance != null ? ShiftManager.Instance.CurrentDay : 1;

        /// <summary>True once he has turned (or the turn day has passed) — no more visits.</summary>
        private bool IsRetired =>
            _state == PostmanVisitState.Mutant || CurrentDay > _mutateDay || NpcMutantPool.HasTurned(_mutantPoolId);

        // _modelVariant value meaning "show the Mutated Version".
        private const int MutatedVariant = -1;

        // Base Version child for the day (Day 1 = 0, ...); from the mutation day on he looks fully mutated.
        private int ModelVariantForDay(int day) => day >= _mutateDay ? MutatedVariant : Mathf.Max(0, day - 1);

        private void HandleDayStart()
        {
            if (!IsServer) return;
            ResetForNewDay();

            if (_state == PostmanVisitState.Mutant || IsRetired || CurrentDay == _endOfShiftArrivalDay) return;
            StartCoroutine(ArriveAfterDayStartDelay(_visitGeneration));
        }

        private IEnumerator ArriveAfterDayStartDelay(int generation)
        {
            yield return new WaitForSeconds(_dayStartArrivalDelay);
            if (generation != _visitGeneration) yield break; // another day start happened meanwhile
            BeginVisit();
        }

        private void HandleLastSuspectProcessed()
        {
            // Day 1's scheduled arrival, and a fallback if a later day's arrival never started.
            if (!IsServer) return;
            if (_visitedToday || IsRetired) return;
            BeginVisit();
        }

        /// <summary>Server-only. Starts today's visit immediately (also usable from debug tools).</summary>
        public void BeginVisit()
        {
            if (!IsServer || _visitedToday || IsRetired) return;
            if (_spawnPos == null || _placingMailPos == null || _finalPos == null)
            {
                Debug.LogWarning("[PostmanVisitController] Route transforms are not assigned.", this);
                return;
            }

            _visitedToday = true;
            _conversationDoneToday = false;
            Debug.Log($"[PostmanVisitController] Day {CurrentDay} visit starting (model variant {_modelVariant.Value}).", this);

            if (_visitRoutine != null) StopCoroutine(_visitRoutine);
            _visitRoutine = StartCoroutine(VisitSequence());
        }

        private void ResetForNewDay()
        {
            // A turned postman belongs to MutantEnemy (which despawns him at day transitions).
            if (_state == PostmanVisitState.Mutant) return;

            if (_visitRoutine != null)
            {
                StopCoroutine(_visitRoutine);
                _visitRoutine = null;
            }

            _character.StopNavigation();
            if (!_character.AreVisualsHidden)
                _character.SetLocomotionState(false);
            _character.SetInteractionLockedServer(true);

            _visitGeneration++;
            _visitedToday = false;
            _conversationDoneToday = false;
            _state = PostmanVisitState.Hidden;
            _visible.Value = false;
            _modelVariant.Value = ModelVariantForDay(CurrentDay);

            if (_spawnPos != null)
                TeleportTo(_spawnPos.position, _spawnPos.rotation);
        }

        // ── Visit sequence (server) ──────────────────────────────────────────────

        private IEnumerator VisitSequence()
        {
            _state = PostmanVisitState.Arriving;
            _character.SetInteractionLockedServer(true);

            ShowArrivalNotificationClientRpc();

            // Place him while still hidden, then give NetworkTransform a moment to settle on
            // clients so they don't see him slide in from his previous position.
            TeleportTo(_spawnPos.position, _spawnPos.rotation);
            yield return new WaitForSeconds(0.3f);

            _visible.Value = true;
            yield return new WaitForSeconds(_fadeInDuration);

            yield return WalkTo(_placingMailPos.position);
            yield return TurnTo(_placingMailPos.rotation);

            _character.FireAnimatorTrigger(_giveTrigger);
            yield return new WaitForSeconds(_giveDropDelay);
            if (_newspaperManager != null)
                _newspaperManager.DeliverTodaysNewspaperServer();
            else
                Debug.LogWarning("[PostmanVisitController] No DailyNewspaperSpawnManager assigned — no newspaper delivered.", this);
            yield return new WaitForSeconds(_giveRecoverTime);

            yield return TurnTo(LookRotationTowards(_finalPos.position));
            yield return WalkTo(_finalPos.position);
            yield return TurnTo(_finalPos.rotation);

            _state = PostmanVisitState.Waiting;
            _character.SetInteractionLockedServer(false);

            float waited = 0f;
            while (waited < _stayDuration || !_conversationDoneToday || _dialogueRunning)
            {
                waited += Time.deltaTime;
                yield return null;
            }

            _state = PostmanVisitState.Leaving;
            _character.SetInteractionLockedServer(true);

            // Someone may have started talking on the frame he locked — let them finish.
            while (_dialogueRunning)
                yield return null;

            yield return TurnTo(LookRotationTowards(_spawnPos.position));
            yield return WalkTo(_spawnPos.position);

            _visible.Value = false;
            yield return new WaitForSeconds(_fadeOutDuration);

            _state = PostmanVisitState.Hidden;
            _visitRoutine = null;
        }

        private IEnumerator WalkTo(Vector3 destination)
        {
            bool arrived = false;
            _character.SetLocomotionState(true);
            _character.NavigateTo(destination, () => arrived = true);
            yield return new WaitUntil(() => arrived);
            _character.SetLocomotionState(false);
        }

        private IEnumerator TurnTo(Quaternion target)
        {
            Vector3 euler = target.eulerAngles;
            Quaternion flatTarget = Quaternion.Euler(0f, euler.y, 0f);

            while (Quaternion.Angle(transform.rotation, flatTarget) > 0.5f)
            {
                transform.rotation = Quaternion.RotateTowards(transform.rotation, flatTarget, _turnSpeed * Time.deltaTime);
                yield return null;
            }

            transform.rotation = flatTarget;
        }

        private Quaternion LookRotationTowards(Vector3 point)
        {
            Vector3 direction = point - transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? Quaternion.LookRotation(direction) : transform.rotation;
        }

        private void TeleportTo(Vector3 position, Quaternion rotation)
        {
            if (_character.NavAgent != null && _character.NavAgent.enabled)
                _character.NavAgent.enabled = false;

            if (_networkTransform != null && _networkTransform.IsSpawned && _networkTransform.CanCommitToTransform)
                _networkTransform.Teleport(position, rotation, transform.localScale);
            else
                transform.SetPositionAndRotation(position, rotation);
        }

        // ── Client presentation ──────────────────────────────────────────────────

        [ClientRpc]
        private void ShowArrivalNotificationClientRpc()
        {
            if (!string.IsNullOrEmpty(_arrivalNotification) && UIController.Instance != null)
                UIController.Instance.ShowQueuedNotification(_arrivalNotification, ArrivalNotificationKey);
        }

        // ── Talking ──────────────────────────────────────────────────────────────

        /// <summary>
        /// Local, on the interacting client (via <see cref="SuspectCharacter.Interact"/>). Rejoining
        /// a conversation already in progress is handled by SuspectCharacter before this runs.
        /// </summary>
        public bool TryHandleTalk(SuspectCharacter character)
        {
            RequestTalkServerRpc();
            return true;
        }

        [ServerRpc(RequireOwnership = false)]
        private void RequestTalkServerRpc(ServerRpcParams rpcParams = default)
        {
            if (_state != PostmanVisitState.Waiting || _dialogueRunning) return;

            ScriptedDialogueRunner runner = ScriptedDialogueRunner.Instance;
            if (runner == null || runner.IsServerSequenceActive) return;

            PostmanDayDialogue set = GetDialogueForDay(CurrentDay);
            if (set == null) return;

            bool isConversation = !_conversationDoneToday;
            ScriptedDialogue dialogue = isConversation ? set.conversation : set.farewell;
            if (dialogue == null || dialogue.nodes == null || dialogue.nodes.Length == 0) return;

            int generation = _visitGeneration;
            ulong talkerClientId = rpcParams.Receive.SenderClientId;
            _dialogueRunning = true;

            runner.PlayDialogue(
                _character,
                dialogue,
                () =>
                {
                    if (generation != _visitGeneration) return; // a new day started mid-talk
                    _dialogueRunning = false;
                    if (!isConversation) return;

                    _conversationDoneToday = true;
                    if (CurrentDay == _mutateDay)
                        StartCoroutine(TurnIntoMutantRoutine(talkerClientId));
                },
                // He's always talked to outside: lock the talker in (cursor for choices, camera on
                // him). With joinByInteractionOnly only interacting players are ever pulled in.
                lockOutsidePlayers: true,
                initialParticipantClientId: talkerClientId,
                joinByInteractionOnly: true,
                allowParticipantExit: true,
                hideNonParticipantUI: false,
                // He's a scene NPC, not a booth suspect: always use the world-dialogue camera,
                // even when the talker counts as inside (he stands near the booth).
                useWorldDialogueCamera: true);
        }

        // ── Mutation (server) ────────────────────────────────────────────────────

        /// <summary>
        /// Swaps to the Mutated Version and hands him to <see cref="MutantEnemy"/>, hunting the player
        /// who talked to him — the same handoff as a full mutant that breached the booth window
        /// (non-interactable, mutant voice, chase/attack, flees when beaten, dies only to fire).
        /// </summary>
        private IEnumerator TurnIntoMutantRoutine(ulong talkerClientId)
        {
            _state = PostmanVisitState.Mutant;
            if (_visitRoutine != null)
            {
                StopCoroutine(_visitRoutine);
                _visitRoutine = null;
            }

            _character.SetInteractionLockedServer(true);
            _character.StopNavigation();
            _character.SetLocomotionState(false);
            NpcMutantPool.MarkTurned(_mutantPoolId);

            yield return new WaitForSeconds(_mutateDelay);

            _character.SetImmuneToDamage(false);
            _character.ActivateAsLegacyMutant(GetPlayerTransform(talkerClientId));
            NpcMutantPool.TrackInstance(_mutantPoolId, _character);
            Debug.Log("[PostmanVisitController] Givi has turned into a full mutant.", this);
        }

        private static Transform GetPlayerTransform(ulong clientId)
        {
            NetworkManager nm = NetworkManager.Singleton;
            if (nm != null && nm.ConnectedClients.TryGetValue(clientId, out NetworkClient client) && client.PlayerObject != null)
                return client.PlayerObject.transform;
            return null;
        }

        /// <summary>Exact day match, else the highest authored day below it, else the first entry.</summary>
        private PostmanDayDialogue GetDialogueForDay(int day)
        {
            if (_dayDialogues == null || _dayDialogues.Length == 0) return null;

            PostmanDayDialogue bestBelow = null;
            foreach (PostmanDayDialogue set in _dayDialogues)
            {
                if (set == null) continue;
                if (set.day == day) return set;
                if (set.day < day && (bestBelow == null || set.day > bestBelow.day)) bestBelow = set;
            }

            return bestBelow ?? _dayDialogues[0];
        }

        private void HandleModelVariantChanged(int previous, int current) => ApplyModelVariant(current);

        private void ApplyModelVariant(int variant)
        {
            if (variant == MutatedVariant) _character.ShowMutatedVisualsLocal();
            else _character.ShowBaseVariantLocal(variant);
        }

        private void HandleVisibleChanged(bool previous, bool current)
        {
            if (current == previous) return;
            bool hasFade = _fade != null;

            if (current)
            {
                if (hasFade) _fade.SetFade(0f);
                _character.SetVisualsHiddenLocal(false);
                if (hasFade) _fade.FadeTo(1f, _fadeInDuration);
            }
            else if (hasFade)
            {
                _fade.FadeTo(0f, _fadeOutDuration, () =>
                {
                    // Only hide if nothing flipped him visible again mid-fade.
                    if (!_visible.Value) _character.SetVisualsHiddenLocal(true);
                });
            }
            else
            {
                _character.SetVisualsHiddenLocal(true);
            }
        }

        private void ApplyVisibilityInstant(bool visible)
        {
            if (_fade != null) _fade.SetFade(visible ? 1f : 0f);
            _character.SetVisualsHiddenLocal(!visible);
        }
    }
}
