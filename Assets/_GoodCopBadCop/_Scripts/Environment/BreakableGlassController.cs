using DG.Tweening;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Manages the breakable booth window glass.
///
/// Tracks cumulative hit damage across mutant visits, updates a procedural crack overlay via
/// MaterialPropertyBlock, and handles the full transition to the broken state when smashed through.
///
/// Networking model
/// ----------------
/// The authoritative damage value lives in <see cref="GlobalHostVariables.glassHits"/>, a
/// server-written NetworkVariable. This controller is a plain MonoBehaviour singleton that
/// *renders* that value:
///
/// – The server calls <see cref="RegisterHit"/> to advance damage; it writes the NetworkVariable.
/// – Every peer receives the change (and the initial value at spawn / on join) via
///   <see cref="GlobalHostVariables.GlassHitsChanged"/> and applies it in
///   <see cref="ApplyGlassState"/>, which is idempotent.
/// – On connected clients the replicated value is the ONLY thing that changes the glass state.
///   The <see cref="OnHitByMutant"/> / <see cref="ApplySmash"/> / <see cref="ResetGlass"/> ClientRpc
///   entry points only play feedback there. The shatter and repair effects play when the replicated
///   value changes live. A dropped, late or duplicated RPC (any player count, late joiners) can
///   therefore never leave a client showing a different window than the host.
///
/// This replaces the previous ClientRpc-only approach, where crack progress, the smashed state and
/// the repair interactable's visibility could permanently diverge between clients: RPCs sent while a
/// client was still connecting were dropped, late joiners were never told anything at all, and each
/// peer restored the state from its OWN local save file rather than the host's.
/// </summary>
public class BreakableGlassController : MonoBehaviour
{
    public static BreakableGlassController Instance { get; private set; }

    // ── Serialized fields ──────────────────────────────────────────────────────

    [Header("References")]
    [Tooltip("The intact glass mesh child (MeshFilter + MeshRenderer + MeshCollider).")]
    [SerializeField] private GameObject _normalGlass;

    [Tooltip("The pre-shattered glass pieces child. Kept inactive until fully smashed.")]
    [SerializeField] private GameObject _brokenGlass;

    [Tooltip("Prefab used to re-instantiate the broken glass after a repair purchase. " +
             "Create this prefab from the Broken Glass scene child — ensure its root is inactive so " +
             "AddForceOnAwake fires only when the glass actually shatters.")]
    [SerializeField] private GameObject _brokenGlassPrefab;

    [Tooltip("The purchase interactable shown when the glass is smashed. " +
             "Should start inactive in the scene; activated automatically when the glass breaks.")]
    [SerializeField] private WorldPurchaseActionInteractable _repairInteractable;

    [Tooltip("Material using the GoodCopBadCop/GlassCrackOverlay shader. " +
             "Instantiated as a crack overlay at runtime on top of the normal glass mesh.")]
    [SerializeField] private Material _crackMaterial;

    [Header("Health")]
    [Tooltip("Total hits required to smash through the glass. Default 4 gives three " +
             "intermediate damage stages plus the final smash.")]
    [SerializeField] [Min(1)] private int _maxHits = 4;

    [Header("Hit Feedback")]
    [Tooltip("Sound played each time the mutant lands an intermediate hit on the glass.")]
    [SerializeField] private AudioClip _hitClip;

    [Tooltip("Additional sound layered on top of _hitClip when the glass is damaged but not fully broken.")]
    [SerializeField] private AudioClip _partialBreakClip;

    [Tooltip("Volume scale for the partial break clip.")]
    [SerializeField] [Range(0f, 2f)] private float _partialBreakVolume = 1f;

    [Tooltip("Sound played when the glass fully shatters.")]
    [SerializeField] private AudioClip _smashClip;

    [Tooltip("Volume scale for both hit and smash clips.")]
    [SerializeField] [Range(0f, 2f)] private float _hitVolume = 1f;

    [Tooltip("The glass Transform to shake on each hit. Leave empty to use _normalGlass.")]
    [SerializeField] private Transform _shakeTarget;

    [Tooltip("Duration of the shake per hit in seconds.")]
    [SerializeField] private float _shakeDuration = 0.20f;

    [Tooltip("Maximum positional offset during the shake.")]
    [SerializeField] private float _shakeStrength = 0.03f;

    [Tooltip("Oscillation count during the shake.")]
    [SerializeField] private int _shakeVibrato = 14;

    [Header("Repair Feedback")]
    [Tooltip("Sound played on all clients when the glass is successfully repaired.")]
    [SerializeField] private AudioClip _repairClip;

    [Tooltip("Volume scale for the repair sound.")]
    [SerializeField] [Range(0f, 2f)] private float _repairVolume = 1f;

    [Tooltip("Particle system played on all clients when the glass is repaired. " +
             "Assign the in-scene RepairParticles child of this GameObject. " +
             "Its GameObject is kept deactivated except while the repair effect is playing.")]
    [SerializeField] private ParticleSystem _repairParticles;

    [Tooltip("Seconds the repair particles' GameObject stays active before being deactivated again. " +
             "Should comfortably cover the longest sub-effect's duration.")]
    [SerializeField] [Min(0.1f)] private float _repairParticlesActiveDuration = 3f;

    [Header("Broken Glass Despawn")]
    [Tooltip("Seconds after shattering before the broken glass pieces are destroyed.")]
    [SerializeField] private float _brokenGlassDespawnDelay = 3f;

    // ── Private state ──────────────────────────────────────────────────────────

    /// <summary>
    /// Local mirror of the authoritative <see cref="GlobalHostVariables.glassHits"/> value.
    /// Kept in sync on every peer so <see cref="IsSmashed"/> and <see cref="CurrentHits"/> answer
    /// identically everywhere; also serves as the standalone value for offline / editor play,
    /// where there is no live session to replicate through.
    /// </summary>
    private int _hits;

    private bool _saveRestoreComplete;

    /// <summary>Guards the one-shot shatter transition so it can never play twice per break.</summary>
    private bool _smashVisualsApplied;

    private MeshRenderer _crackRenderer;
    private MaterialPropertyBlock _mpb;
    private AudioSource _audioSource;
    private Tween _shakeTween;
    private Coroutine _despawnCoroutine;
    private Coroutine _repairParticlesCoroutine;

    // Cached spawn data so the broken glass can be re-instantiated at the original transform.
    private Transform  _brokenGlassParent;
    private Vector3    _brokenGlassLocalPos;
    private Quaternion _brokenGlassLocalRot;

    private static readonly int CrackProgressId = Shader.PropertyToID("_CrackProgress");

    // ── Properties ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Current damage on the glass. Reads the replicated value while a session is live, so it is
    /// identical on the host and every client (including late joiners), and falls back to the local
    /// counter offline.
    /// </summary>
    public int CurrentHits =>
        GlobalHostVariables.IsGlassStateNetworked ? GlobalHostVariables.CurrentGlassHits : _hits;

    /// <summary>
    /// True when the glass has received enough hits to be fully smashed. Now reliable on every
    /// peer, not just the server, because it is derived from the replicated hit count.
    /// </summary>
    public bool IsSmashed => CurrentHits >= _maxHits;

    /// <summary>
    /// True on all clients while the intact glass pane is visible.
    /// Used by <see cref="ScriptedDialogueRunner"/> to suppress the face-cam when the
    /// glass would obscure the close-up view.
    /// </summary>
    public bool IsWindowVisible => _normalGlass != null && _normalGlass.activeSelf;

    /// <summary>Maximum hits before the glass shatters.</summary>
    public int MaxHits => _maxHits;

    /// <summary>Normalised crack progress in 0–1, matching what the overlay shader is showing.</summary>
    public float DamageProgress => _maxHits > 0 ? Mathf.Clamp01((float)CurrentHits / _maxHits) : 0f;

    /// <summary>
    /// Which side of the pane the rolling shutter sits on, relative to the pane's forward axis:
    /// +1 / -1, or 0 while not yet resolved. The shutter only rolls vertically, so the side never
    /// changes and is resolved once from the shutter's renderer bounds.
    /// </summary>
    private int _shutterSide;

    /// <summary>
    /// True when the rolling shutter is closed AND <paramref name="attackerPosition"/> is on the
    /// shutter's side of the pane, i.e. the closed shutter stands between the attacker and the
    /// glass. Player weapons must treat the glass as unhittable in that case. The shutter has no
    /// collider, so this geometric check is what makes it shield the pane.
    /// </summary>
    public bool IsShieldedFrom(Vector3 attackerPosition)
    {
        if (!TryGetClosedShutterPlane(out Bounds paneBounds, out Vector3 towardShutter)) return false;
        return Vector3.Dot(attackerPosition - paneBounds.center, towardShutter) > 0f;
    }

    /// <summary>
    /// How far (metres) beyond the pane's edges the closed shutter is still treated as covering
    /// the opening, so a line clipping the frame edge doesn't slip past.
    /// </summary>
    private const float ShutterCoverMargin = 0.15f;

    /// <summary>
    /// True when the rolling shutter is closed and the segment <paramref name="from"/> →
    /// <paramref name="to"/> passes through the window opening (from either side). Works whether
    /// the glass is intact or smashed. The shutter has no collider, so line-of-sight checks (e.g.
    /// melee) must call this to stop reaching through a closed shutter.
    /// </summary>
    public bool IsLineBlockedByShutter(Vector3 from, Vector3 to)
    {
        if (!TryGetClosedShutterPlane(out Bounds paneBounds, out Vector3 towardShutter)) return false;

        float fromDot = Vector3.Dot(from - paneBounds.center, towardShutter);
        float toDot   = Vector3.Dot(to   - paneBounds.center, towardShutter);
        if (fromDot * toDot >= 0f) return false; // Doesn't cross the window plane.

        Vector3 crossing = Vector3.Lerp(from, to, fromDot / (fromDot - toDot));
        paneBounds.Expand(ShutterCoverMargin * 2f);
        return paneBounds.Contains(crossing);
    }

    /// <summary>
    /// When the shutter exists and is closed, returns the pane's world bounds and the pane normal
    /// pointing toward the shutter's side. The pane bounds come from the mesh, not the renderer,
    /// so they stay valid while the intact pane is hidden (smashed).
    /// </summary>
    private bool TryGetClosedShutterPlane(out Bounds paneBounds, out Vector3 towardShutter)
    {
        paneBounds    = default;
        towardShutter = Vector3.zero;

        ShutterController shutter = ShutterController.Instance;
        if (shutter == null || shutter.IsOpen || _normalGlass == null) return false;

        Transform pane = _normalGlass.transform;
        paneBounds = GetPaneWorldBounds();

        if (_shutterSide == 0)
        {
            Vector3 shutterCenter = GetBoundsCenter(shutter.gameObject, shutter.transform.position);
            float shutterDot = Vector3.Dot(shutterCenter - paneBounds.center, pane.forward);
            if (Mathf.Abs(shutterDot) < 0.0001f) return false;
            _shutterSide = shutterDot > 0f ? 1 : -1;
        }

        towardShutter = pane.forward * _shutterSide;
        return true;
    }

    /// <summary>World-space bounds of the intact pane mesh, valid even while the pane is inactive.</summary>
    private Bounds GetPaneWorldBounds()
    {
        Transform pane = _normalGlass.transform;
        MeshFilter mf = _normalGlass.GetComponent<MeshFilter>();
        if (mf == null || mf.sharedMesh == null)
            return new Bounds(pane.position, Vector3.zero);

        Bounds local = mf.sharedMesh.bounds;
        Vector3 c = local.center;
        Vector3 e = local.extents;
        Bounds world = new Bounds(pane.TransformPoint(c), Vector3.zero);
        for (int x = -1; x <= 1; x += 2)
        for (int y = -1; y <= 1; y += 2)
        for (int z = -1; z <= 1; z += 2)
            world.Encapsulate(pane.TransformPoint(c + Vector3.Scale(e, new Vector3(x, y, z))));
        return world;
    }

    private static Vector3 GetBoundsCenter(GameObject root, Vector3 fallback)
    {
        // Inactive/disabled renderers report empty bounds at the origin, so only use live ones.
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
        bool found = false;
        Bounds bounds = default;
        foreach (Renderer r in renderers)
        {
            if (!r.enabled || r is ParticleSystemRenderer) continue;
            if (!found) { bounds = r.bounds; found = true; }
            else        bounds.Encapsulate(r.bounds);
        }
        return found ? bounds.center : fallback;
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    private void Awake()
    {
        Instance = this;
        _audioSource = GetComponent<AudioSource>();
        _mpb = new MaterialPropertyBlock();

        if (_normalGlass != null)
            BuildCrackOverlay();

        // Cache broken glass spawn data so it can be re-instantiated after despawn.
        if (_brokenGlass != null)
        {
            _brokenGlassParent   = _brokenGlass.transform.parent;
            _brokenGlassLocalPos = _brokenGlass.transform.localPosition;
            _brokenGlassLocalRot = _brokenGlass.transform.localRotation;
        }

        // The repair particle hierarchy (root sparks system plus its sub-emitters) is only
        // needed for a few seconds after a repair purchase — keep it deactivated the rest
        // of the time so its particle systems and renderers don't tick every frame.
        if (_repairParticles != null)
            _repairParticles.gameObject.SetActive(false);

        // Start fully transparent / undamaged.
        RefreshCrackOverlay(0);
    }

    /// <summary>
    /// Subscribes to the authoritative damage value. Done in OnEnable rather than Start because
    /// this GameObject is deactivated during the main menu (see <see cref="MainMenuSceneSetup"/>)
    /// and must re-adopt the host's state every time it comes back.
    /// </summary>
    private void OnEnable()
    {
        GlobalHostVariables.GlassHitsChanged += HandleNetworkGlassHits;

        // Pull the current value too: the singleton may already have spawned (and therefore already
        // fired its initial event) before this object was enabled. Adopted silently — whatever
        // happened while this object was inactive is history, not a live transition.
        if (GlobalHostVariables.IsGlassStateNetworked)
            HandleNetworkGlassHits(GlobalHostVariables.CurrentGlassHits, true);
    }

    private void OnDisable()
    {
        GlobalHostVariables.GlassHitsChanged -= HandleNetworkGlassHits;
    }

    /// <summary>
    /// Restores glass state from the save file after one frame, giving the network manager time to
    /// fully initialise before the host publishes it to everyone.
    /// </summary>
    private System.Collections.IEnumerator Start()
    {
        yield return null;
        RefreshFromSave();
    }

    /// <summary>
    /// Explicitly re-runs the save-state restore. Safe to call from outside if the internal
    /// Start() coroutine was interrupted (e.g. the GameObject was deactivated during the main menu).
    /// No-ops if the restore has already completed.
    /// </summary>
    public void RefreshFromSave()
    {
        if (_saveRestoreComplete) return;
        _saveRestoreComplete = true;
        RestoreGlassStateFromSave();
    }

    private void BuildCrackOverlay()
    {
        if (_crackMaterial == null) return;

        var overlay = new GameObject("CrackOverlay");
        overlay.transform.SetParent(_normalGlass.transform, false);
        overlay.transform.localPosition = Vector3.zero;
        overlay.transform.localRotation = Quaternion.identity;
        overlay.transform.localScale    = Vector3.one;

        // Share the same mesh as the normal glass.
        var sourceMf = _normalGlass.GetComponent<MeshFilter>();
        if (sourceMf != null)
        {
            var mf = overlay.AddComponent<MeshFilter>();
            mf.sharedMesh = sourceMf.sharedMesh;
        }

        _crackRenderer = overlay.AddComponent<MeshRenderer>();
        _crackRenderer.sharedMaterial            = _crackMaterial;
        _crackRenderer.shadowCastingMode          = ShadowCastingMode.Off;
        _crackRenderer.receiveShadows             = false;
        _crackRenderer.lightProbeUsage            = LightProbeUsage.Off;
        _crackRenderer.reflectionProbeUsage       = ReflectionProbeUsage.Off;
        _crackRenderer.allowOcclusionWhenDynamic  = false;
    }

    // ── Public server-side API ─────────────────────────────────────────────────

    /// <summary>
    /// Registers one hit on the server and returns the new total hit count.
    /// Writes the authoritative NetworkVariable, so every client — including any that joins later —
    /// converges on this value even if the accompanying feedback ClientRpc is missed.
    /// Must only be called on the server.
    /// </summary>
    public int RegisterHit()
    {
        if (IsSmashed) return CurrentHits;

        SetHitsAuthoritative(CurrentHits + 1);
        return _hits;
    }

    /// <summary>
    /// Debug helper — applies the fully smashed state. When called on the host it also publishes the
    /// smashed value, so connected clients follow instead of desyncing from the cheat.
    /// </summary>
    public void ForceSmash()
    {
        SetHitsAuthoritative(_maxHits);
        ApplySmash();
    }

    /// <summary>
    /// Server-only. Writes <paramref name="hits"/> to the replicated glass state, updates the local
    /// mirror, and persists it. Silently degrades to a purely local update when there is no live
    /// session (offline / editor play), which keeps single-player behaviour unchanged.
    /// </summary>
    private void SetHitsAuthoritative(int hits)
    {
        // A connected client must never write its local mirror: that is exactly how a client could
        // end up believing a state the host never had. Clients follow the replicated value only.
        if (!HasStateAuthority()) return;

        int clamped = Mathf.Clamp(hits, 0, _maxHits);
        _hits = clamped;

        GlobalHostVariables.Instance?.SetGlassHits(clamped);
        PersistGlassState(clamped);
    }

    /// <summary>
    /// True on a connected (non-host) client while the replicated glass state is live. On such a
    /// peer, RPC entry points only play feedback; state comes exclusively from
    /// <see cref="HandleNetworkGlassHits"/>.
    /// </summary>
    private static bool IsNetworkFollower => !HasStateAuthority() && GlobalHostVariables.IsGlassStateNetworked;

    // ── Public client-side (visual) API ───────────────────────────────────────

    /// <summary>
    /// Plays hit feedback and, on the host/offline, updates the crack overlay to the given hit count.
    /// Called on all clients via ClientRpc after each intermediate hit. Connected clients get the
    /// crack progress from the replicated value instead, so a late/stale RPC can't override it.
    /// </summary>
    public void OnHitByMutant(int hitCount)
    {
        int clamped = Mathf.Clamp(hitCount, 0, _maxHits);

        if (!IsNetworkFollower)
        {
            _hits = clamped;
            ApplyGlassState(_hits, animateSmash: false);
        }

        PlayHitFeedback(clamped);
    }

    /// <summary>
    /// Transitions to the fully smashed state: hides the intact glass, activates the broken shards,
    /// plays the smash sound, shows the repair interactable, and schedules the shards for destruction.
    /// Called on all clients via ClientRpc on the final blow. On connected clients this is a no-op —
    /// the replicated value crossing the smash threshold plays the exact same transition, once.
    /// </summary>
    public void ApplySmash()
    {
        if (IsNetworkFollower) return;

        _hits = _maxHits;
        ApplyGlassState(_maxHits, animateSmash: true);
    }

    /// <summary>
    /// Repairs the glass. Invoked on every peer from WorldPurchaseActionInteractable's purchase
    /// ClientRpc (or directly when offline).
    ///
    /// Only the host/offline peer actually repairs: it publishes 0 to the replicated state and plays
    /// the repair presentation. Every connected client — including ones that never received the
    /// purchase RPC — sees the replicated 0 arrive and runs the same repair presentation from
    /// <see cref="HandleNetworkGlassHits"/>, so the pane always reappears for all players.
    /// </summary>
    public void ResetGlass()
    {
        if (IsNetworkFollower) return;

        bool wasDamaged = _hits > 0;
        SetHitsAuthoritative(0);

        ApplyGlassState(0, animateSmash: false);
        if (wasDamaged || !GlobalHostVariables.IsGlassStateNetworked)
            PlayRepairFeedback();
    }

    // ── State application ──────────────────────────────────────────────────────

    /// <summary>
    /// Receives the authoritative hit count on every peer, from
    /// <see cref="GlobalHostVariables.GlassHitsChanged"/>.
    ///
    /// An initial sync (spawn / late join / re-enable) is applied silently because it represents
    /// "this is how the window already looks". A live change that crosses the smash threshold plays
    /// the shatter, and a live change back to 0 plays the repair presentation. On the host both are
    /// already handled locally before the value is written, so they are never doubled there.
    /// </summary>
    private void HandleNetworkGlassHits(int hits, bool isInitialSync)
    {
        int clamped = Mathf.Clamp(hits, 0, _maxHits);

        // The host can restore a saved damage value before the session is running, in which case the
        // freshly spawned NetworkVariable still holds its default 0. The host's value must win, so
        // republish it instead of letting the default silently erase the saved damage — otherwise
        // the host would show a cracked window while every client shows a pristine one.
        if (clamped < _hits && HasStateAuthority() && GlobalHostVariables.IsGlassStateNetworked)
        {
            GlobalHostVariables.Instance.SetGlassHits(_hits);
            return;
        }

        bool liveSmash  = !isInitialSync && clamped >= _maxHits && _hits < _maxHits;
        bool liveRepair = !isInitialSync && clamped == 0 && _hits > 0;

        _hits = clamped;

        ApplyGlassState(clamped, animateSmash: liveSmash);

        if (liveRepair)
            PlayRepairFeedback();
    }

    /// <summary>
    /// The single, idempotent place where glass damage becomes visible state. Safe to call
    /// repeatedly with the same value, and safe to call from both the ClientRpc feedback path and
    /// the NetworkVariable path in either order.
    /// </summary>
    /// <param name="hits">Authoritative hit count to render.</param>
    /// <param name="animateSmash">
    /// True only for a live break: activates the shard pieces, plays the smash sound and schedules
    /// the shard despawn. False just adopts the resulting look, leaving the (already despawned)
    /// shards alone — which is what a late joiner or a save restore needs.
    /// </param>
    private void ApplyGlassState(int hits, bool animateSmash)
    {
        bool smashed = hits >= _maxHits;

        RefreshCrackOverlay(hits);

        if (smashed)
        {
            if (_normalGlass != null)
                _normalGlass.SetActive(false);

            if (animateSmash && !_smashVisualsApplied)
            {
                _smashVisualsApplied = true;

                if (_audioSource != null && _smashClip != null)
                    _audioSource.PlayOneShot(_smashClip, _hitVolume);

                if (_brokenGlass != null)
                {
                    _brokenGlass.SetActive(true);

                    // Destroy the shards after a short delay. Runs on every peer, since the state
                    // that got us here is itself replicated.
                    if (_despawnCoroutine != null) StopCoroutine(_despawnCoroutine);
                    _despawnCoroutine = StartCoroutine(DespawnBrokenGlassCoroutine());
                }
            }
        }
        else
        {
            _smashVisualsApplied = false;

            if (_normalGlass != null && !_normalGlass.activeSelf)
                _normalGlass.SetActive(true);

            // Shards must never be visible on an intact window.
            if (_brokenGlass != null && _brokenGlass.activeSelf)
                _brokenGlass.SetActive(false);
        }

        // Repair is purchasable as soon as the glass shows ANY damage — no need to wait for the
        // final blow. Derived from the replicated count on every peer, so the Purchase Glass object
        // can no longer be active for one player and inactive for another.
        if (hits > 0)
            ShowRepairInteractable();
        else
            _repairInteractable?.SetAvailable(false);
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the glass state to the save file. Guarded to the server/offline so a client can never
    /// overwrite its local save with a value the host disagrees with — the previous version wrote
    /// from every peer and then read it back per-peer at startup, which is how two players could end
    /// up restoring different glass states.
    /// </summary>
    private void PersistGlassState(int hits)
    {
        if (!HasStateAuthority()) return;
        SaveDataManager.Instance?.SetGlassState(hits, hits >= _maxHits);
    }

    /// <summary>
    /// True on the host/server, or when running with no live session (offline / editor play).
    /// </summary>
    private static bool HasStateAuthority()
    {
        var nm = NetworkManager.Singleton;
        return nm == null || !nm.IsListening || nm.IsServer;
    }

    /// <summary>
    /// Reads the saved glass state and re-applies it at session start. Only the authority does this:
    /// the restored value is written to <see cref="GlobalHostVariables.glassHits"/> and replicated,
    /// so every client renders the host's saved state instead of its own.
    /// The broken glass pieces are intentionally left inactive on load — they were despawned in the
    /// previous session and will reappear only on the next smash.
    /// </summary>
    private void RestoreGlassStateFromSave()
    {
        if (!HasStateAuthority()) return;
        if (SaveDataManager.Instance == null) return;

        int savedHits = Mathf.Clamp(SaveDataManager.Instance.GlassHits, 0, _maxHits);
        if (savedHits <= 0) return;

        // Publish before applying so clients and the host adopt the same value from one source.
        SetHitsAuthoritative(savedHits);
        ApplyGlassState(savedHits, animateSmash: false);
    }

    /// <summary>
    /// Waits for <see cref="_brokenGlassDespawnDelay"/> seconds then destroys the broken glass pieces
    /// and pre-instantiates a fresh, inactive replacement ready for the next smash.
    /// </summary>
    private System.Collections.IEnumerator DespawnBrokenGlassCoroutine()
    {
        yield return new WaitForSeconds(_brokenGlassDespawnDelay);

        if (_brokenGlass != null)
        {
            Destroy(_brokenGlass);
            _brokenGlass = null;
        }

        _despawnCoroutine = null;
    }

    /// <summary>
    /// Waits for <see cref="_repairParticlesActiveDuration"/> seconds then deactivates the repair
    /// particles' GameObject, taking its ParticleSystems (and sub-emitters) out of the update loop
    /// until the next repair.
    /// </summary>
    private System.Collections.IEnumerator DeactivateRepairParticlesCoroutine()
    {
        yield return new WaitForSeconds(_repairParticlesActiveDuration);

        if (_repairParticles != null)
            _repairParticles.gameObject.SetActive(false);

        _repairParticlesCoroutine = null;
    }

    /// <summary>
    /// Instantiates a fresh broken glass object as an inactive child, ready for the next smash.
    /// Logs a warning and skips silently if <see cref="_brokenGlassPrefab"/> is not assigned.
    /// </summary>
    private void RespawnBrokenGlass()
    {
        if (_brokenGlass != null) return;

        if (_brokenGlassPrefab == null)
        {
            Debug.LogWarning("[BreakableGlassController] _brokenGlassPrefab is not assigned — " +
                             "broken glass will not respawn after repair. " +
                             "Assign the Broken Glass prefab in the Inspector.");
            return;
        }

        _brokenGlass = Instantiate(_brokenGlassPrefab, _brokenGlassParent);
        _brokenGlass.transform.SetLocalPositionAndRotation(_brokenGlassLocalPos, _brokenGlassLocalRot);

        // Keep it inactive until the next smash triggers the shatter transition.
        _brokenGlass.SetActive(false);
    }

    /// <summary>
    /// Makes the repair/purchase interactable available on the local client, spawning its
    /// NetworkObject on the server if needed. Safe to call multiple times (e.g. once per
    /// intermediate hit and again on the final smash) — spawning and activation are both no-ops
    /// once already done.
    /// </summary>
    private void ShowRepairInteractable()
    {
        if (_repairInteractable == null) return;

        _repairInteractable.SetAvailable(true);

        // The repair interactable's GameObject starts inactive so NGO never auto-spawns its
        // NetworkObject. The server must spawn it explicitly so the purchase ServerRpc / ClientRpc
        // path — and the interactable's own availability NetworkVariable — can route.
        var nm = NetworkManager.Singleton;
        if (nm != null && nm.IsListening && nm.IsServer)
        {
            var repairNetObj = _repairInteractable.NetworkObject;
            if (repairNetObj != null && !repairNetObj.IsSpawned)
                repairNetObj.Spawn(true);   // true = destroyWithScene
        }
    }

    /// <summary>Sets the _CrackProgress material property to reflect the given hit count.</summary>
    private void RefreshCrackOverlay(int hitCount)
    {
        if (_crackRenderer == null) return;

        float progress = _maxHits > 0 ? Mathf.Clamp01((float)hitCount / _maxHits) : 0f;
        _mpb.SetFloat(CrackProgressId, progress);
        _crackRenderer.SetPropertyBlock(_mpb);
    }

    /// <summary>
    /// Cosmetic, damage-free knock on the intact pane (hit sound + shake). Local to the calling
    /// peer — callers broadcast it themselves (e.g. a scripted dialogue line where a suspect bangs
    /// on the glass). No-op while the window is smashed or hidden.
    /// </summary>
    public void PlayKnockFeedback()
    {
        if (!IsWindowVisible) return;

        if (_audioSource != null && _hitClip != null)
            _audioSource.PlayOneShot(_hitClip, _hitVolume);

        var target = _shakeTarget != null ? _shakeTarget : _normalGlass.transform;
        _shakeTween?.Kill(complete: true);
        _shakeTween = target.DOShakePosition(_shakeDuration, _shakeStrength, _shakeVibrato);
    }

    private void PlayHitFeedback(int hitCount)
    {
        if (_audioSource != null && _hitClip != null)
            _audioSource.PlayOneShot(_hitClip, _hitVolume);

        if (_audioSource != null && _partialBreakClip != null && hitCount > 0 && hitCount < _maxHits)
            _audioSource.PlayOneShot(_partialBreakClip, _partialBreakVolume);

        var target = _shakeTarget != null
            ? _shakeTarget
            : (_normalGlass != null ? _normalGlass.transform : null);

        if (target != null)
        {
            _shakeTween?.Kill(complete: true);
            _shakeTween = target.DOShakePosition(_shakeDuration, _shakeStrength, _shakeVibrato);
        }
    }

    /// <summary>
    /// Plays the one-shot repair presentation (pop scale, sound, sparks) and pre-instantiates a
    /// fresh set of shards. Separate from <see cref="ApplyGlassState"/> so silently adopting an
    /// intact window (late join, save restore) doesn't fire repair effects.
    /// </summary>
    private void PlayRepairFeedback()
    {
        // Cancel any in-flight despawn so the leftover shards can be cleaned up deterministically.
        if (_despawnCoroutine != null)
        {
            StopCoroutine(_despawnCoroutine);
            _despawnCoroutine = null;
        }

        if (_brokenGlass != null)
        {
            Destroy(_brokenGlass);
            _brokenGlass = null;
        }

        if (_normalGlass != null)
        {
            _normalGlass.transform.DOKill();
            _normalGlass.transform.DOPunchScale(Vector3.one * 0.12f, 0.35f, 6, 0.5f);
        }

        if (_audioSource != null && _repairClip != null)
            _audioSource.PlayOneShot(_repairClip, _repairVolume);

        if (_repairParticles != null)
        {
            _repairParticles.gameObject.SetActive(true);
            _repairParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            _repairParticles.Play();

            if (_repairParticlesCoroutine != null) StopCoroutine(_repairParticlesCoroutine);
            _repairParticlesCoroutine = StartCoroutine(DeactivateRepairParticlesCoroutine());
        }

        // Pre-instantiate a fresh, inactive broken glass so it is ready for the next smash.
        RespawnBrokenGlass();
    }
}
