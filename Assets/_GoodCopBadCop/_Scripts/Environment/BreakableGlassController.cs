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
/// – The existing <see cref="OnHitByMutant"/> / <see cref="ApplySmash"/> ClientRpc entry points are
///   kept purely for one-shot feedback (sound, shake, shards) and still converge on the same state.
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

    /// <summary>
    /// True once this peer has adopted a replicated value at least once. Used to tell an initial
    /// state adoption (which must NOT replay the shatter animation — a late joiner would see shards
    /// fall for a window that broke minutes ago) apart from a genuine live transition.
    /// </summary>
    private bool _networkStateAdopted;

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
        // fired its initial event) before this object was enabled.
        if (GlobalHostVariables.IsGlassStateNetworked)
            HandleNetworkGlassHits(GlobalHostVariables.CurrentGlassHits);
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
        int clamped = Mathf.Clamp(hits, 0, _maxHits);
        _hits = clamped;
        _networkStateAdopted = true;

        GlobalHostVariables.Instance?.SetGlassHits(clamped);
        PersistGlassState(clamped);
    }

    // ── Public client-side (visual) API ───────────────────────────────────────

    /// <summary>
    /// Updates the crack overlay to the given hit count and plays hit feedback.
    /// Called on all clients via ClientRpc after each intermediate hit.
    /// </summary>
    public void OnHitByMutant(int hitCount)
    {
        // Record the value locally as well so the replicated update that follows is recognised as
        // already-applied and doesn't re-trigger the shatter transition.
        _hits = Mathf.Clamp(hitCount, 0, _maxHits);
        _networkStateAdopted = true;

        ApplyGlassState(_hits, animateSmash: false);
        PlayHitFeedback();
    }

    /// <summary>
    /// Transitions to the fully smashed state on all clients:
    /// hides the intact glass, activates the broken shards, plays the smash sound,
    /// shows the repair interactable, and schedules the broken pieces for destruction.
    /// Called on all clients via ClientRpc on the final blow.
    /// </summary>
    public void ApplySmash()
    {
        _hits = _maxHits;
        _networkStateAdopted = true;

        ApplyGlassState(_maxHits, animateSmash: true);
    }

    /// <summary>
    /// Resets the glass to full health on all clients:
    /// cancels any pending despawn, destroys leftover broken shards, re-enables the intact pane,
    /// and pre-instantiates a fresh (inactive) broken glass ready for the next smash.
    /// Invoked on every peer from WorldPurchaseActionInteractable's purchase ClientRpc; the host
    /// additionally publishes the reset so late joiners never see a stale broken window.
    /// </summary>
    public void ResetGlass()
    {
        // Server/offline: publish and persist the repair. On a client this is a no-op, and the
        // host's replicated 0 arrives independently — both paths end in the same visual state.
        SetHitsAuthoritative(0);

        ApplyGlassState(0, animateSmash: false);
        PlayRepairFeedback();
    }

    // ── State application ──────────────────────────────────────────────────────

    /// <summary>
    /// Receives the authoritative hit count on every peer, from
    /// <see cref="GlobalHostVariables.GlassHitsChanged"/>.
    ///
    /// The very first value adopted after enabling is applied silently (no shatter animation, no
    /// audio) because it represents "this is how the window already looks" — a late joiner must not
    /// watch a break that happened before they connected. Later changes that cross the smash
    /// threshold do play the transition, which also covers a dropped feedback ClientRpc.
    /// </summary>
    private void HandleNetworkGlassHits(int hits)
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

        bool isLiveTransition = _networkStateAdopted && clamped >= _maxHits && _hits < _maxHits;

        _hits = clamped;
        _networkStateAdopted = true;

        ApplyGlassState(clamped, animateSmash: isLiveTransition);
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

    private void PlayHitFeedback()
    {
        if (_audioSource != null && _hitClip != null)
            _audioSource.PlayOneShot(_hitClip, _hitVolume);

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
