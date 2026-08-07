using System.Collections.Generic;
using UnityEngine;
using Obi;

/// <summary>
/// Keeps an ObiSolver's simulation disabled by default, and only enables it
/// while the item is actively being held by a player within range of the
/// camera. This is the common case for held tools like the Mop: the rope only
/// needs to actually simulate while it's in the player's hands and visible up
/// close - at all other times (dropped, thrown, sitting somewhere, held but
/// far away/by another player) it should stay disabled to avoid paying the
/// (often expensive) per-frame Obi solver cost.
///
/// When the item is dropped/thrown, the solver is kept running for a short
/// "settle" grace period (<see cref="settleDuration"/>) - or until the
/// attached Rigidbody falls asleep, whichever comes first - so the rope can
/// visually fall/settle into its resting pose before being frozen, rather
/// than freezing it mid-fling.
///
/// While the solver is disabled, each ObiRopeExtrudedRenderer under it has its
/// current shape baked into a plain static Mesh (via ObiExtrudedRopeRenderSystem.
/// BakeMesh) shown through an ordinary MeshRenderer, and the live Obi renderer
/// is disabled. This keeps the rope visible in its last simulated pose at
/// zero ongoing simulation/rendering cost from Obi itself - unlike simply
/// resubmitting ObiSolver.Render() every frame, which still pays for
/// interpolation/visibility work each frame.
/// </summary>
[RequireComponent(typeof(ObiSolver))]
public class ObiSolverDistanceCulling : MonoBehaviour
{
    [Header("Held/Range Settings")]
    [Tooltip("The pickable item this solver belongs to. While it's held and within " +
             "range, the solver runs live; otherwise it stays disabled (mesh baked). " +
             "If unset, will search parents for one.")]
    [SerializeField] private PickableObject pickableObject;

    [Tooltip("Distance from the camera beyond which the Obi solver is disabled, " +
             "even while held (e.g. held by a remote player far from the local camera).")]
    [SerializeField] private float disableDistance = 60f;

    [Tooltip("Distance from the camera below which the Obi solver is re-enabled while held. " +
             "Should be less than disableDistance to avoid flicker at the boundary.")]
    [SerializeField] private float enableDistance = 55f;

    [Header("Settle Settings (after drop/throw)")]
    [Tooltip("Rigidbody to check for rest state. If unset, will search parents for one.")]
    [SerializeField] private Rigidbody rigidbodyToCheck;

    [Tooltip("How long (in seconds) to keep the solver running after the item stops being " +
             "held, so the rope can visually settle before being frozen. The solver freezes " +
             "sooner than this if the Rigidbody falls asleep first.")]
    [SerializeField] private float settleDuration = 2f;

    [Header("Target")]
    [Tooltip("How often (in seconds) to check distance and poll for the active camera. " +
             "Held-state and rest-state are checked every frame regardless, since they're cheap.")]
    [SerializeField] private float checkInterval = 0.5f;

    private ObiSolver _obiSolver;
    private float _nextDistanceCheckTime;
    private bool _frozen;
    private bool _wasHeld;
    private float _settleTimer;
    private bool _settling;
    private bool _lastInRange = true;
    private bool _warmedUp;

    private class FrozenRope
    {
        public ObiRopeExtrudedRenderer liveRenderer;
        public MeshFilter meshFilter;
        public MeshRenderer meshRenderer;
        public Mesh bakedMesh;
    }

    private readonly List<FrozenRope> _frozenRopes = new List<FrozenRope>();

    private void Awake()
    {
        _obiSolver = GetComponent<ObiSolver>();

        if (pickableObject == null)
        {
            pickableObject = GetComponentInParent<PickableObject>();
        }

        if (rigidbodyToCheck == null)
        {
            rigidbodyToCheck = GetComponentInParent<Rigidbody>();
        }

        foreach (ObiRopeExtrudedRenderer liveRenderer in GetComponentsInChildren<ObiRopeExtrudedRenderer>(true))
        {
            _frozenRopes.Add(new FrozenRope { liveRenderer = liveRenderer });
        }
    }

    private void OnEnable()
    {
        _nextDistanceCheckTime = Time.time;
        _wasHeld = pickableObject != null && pickableObject.IsHeld;
        _settling = false;
        _settleTimer = 0f;
    }

    private System.Collections.IEnumerator Start()
    {
        // Let the solver run for a couple of frames first so the ropes have an actual
        // simulated shape (their rest pose) to bake, rather than baking whatever
        // degenerate/zeroed state they're in before the solver has stepped even once.
        yield return null;
        yield return null;

        _warmedUp = true;

        if (!_frozen && !(pickableObject != null && pickableObject.IsHeld))
        {
            Freeze();
            _obiSolver.enabled = false;
        }
    }

    private void OnDestroy()
    {
        foreach (FrozenRope rope in _frozenRopes)
        {
            if (rope.bakedMesh != null)
            {
                Destroy(rope.bakedMesh);
            }
        }
    }

    private void Update()
    {
        // Skip all culling logic for the first couple of frames - Start() is baking an
        // initial mesh from the solver's actual rest pose so the rope doesn't briefly
        // vanish/look empty before there's anything valid to freeze into.
        if (!_warmedUp)
        {
            return;
        }

        bool isHeld = pickableObject != null && pickableObject.IsHeld;

        // Held state just changed - reset the settle timer whenever we transition
        // from held to not-held (dropped/thrown), so the grace period always starts
        // fresh from the moment of release rather than from whenever this happened
        // to first notice it.
        if (isHeld != _wasHeld)
        {
            _settling = !isHeld;
            _settleTimer = 0f;
            _wasHeld = isHeld;
        }

        if (isHeld)
        {
            bool inRange = CheckInRange();

            if (inRange)
            {
                SetSolverEnabled(true);
            }
            else
            {
                // Held but out of range (e.g. by a remote player far from the local
                // camera) - no need to keep simulating for a view nobody is looking at.
                SetSolverEnabled(false);
            }

            return;
        }

        // Not held: keep the solver running for a short settle window (or until the
        // Rigidbody falls asleep, whichever comes first) so the rope can visually fall
        // into its resting pose, then freeze it. Otherwise the solver just stays frozen.
        if (_settling)
        {
            SetSolverEnabled(true);

            _settleTimer += Time.deltaTime;

            bool atRest = rigidbodyToCheck != null
                && !rigidbodyToCheck.isKinematic
                && rigidbodyToCheck.IsSleeping();

            if (atRest || _settleTimer >= settleDuration)
            {
                _settling = false;
                SetSolverEnabled(false);
            }
        }
        else
        {
            SetSolverEnabled(false);
        }
    }

    /// <summary>
    /// Throttled distance check against the active camera, with hysteresis between
    /// disableDistance/enableDistance to avoid flicker right at the boundary.
    /// </summary>
    private bool CheckInRange()
    {
        if (Time.time < _nextDistanceCheckTime)
        {
            return _lastInRange;
        }

        _nextDistanceCheckTime = Time.time + checkInterval;

        Camera targetCamera = ResolveActiveCamera();
        if (targetCamera == null)
        {
            // No camera resolved - default to in-range rather than incorrectly
            // culling a held item nobody's tracking a view for yet.
            _lastInRange = true;
            return true;
        }

        float distance = Vector3.Distance(transform.position, targetCamera.transform.position);

        if (_lastInRange && distance >= disableDistance)
        {
            _lastInRange = false;
        }
        else if (!_lastInRange && distance <= enableDistance)
        {
            _lastInRange = true;
        }

        return _lastInRange;
    }

    private void SetSolverEnabled(bool solverEnabled)
    {
        if (solverEnabled)
        {
            if (!_frozen)
            {
                return;
            }

            _obiSolver.enabled = true;
            Unfreeze();
        }
        else
        {
            if (_frozen)
            {
                return;
            }

            Freeze();
            _obiSolver.enabled = false;
        }
    }

    /// <summary>
    /// Bakes each live rope renderer's current shape into a static mesh shown
    /// via a plain MeshRenderer, then disables the live Obi renderer so it
    /// stops costing anything once the solver itself is disabled.
    /// </summary>
    private void Freeze()
    {
        foreach (FrozenRope rope in _frozenRopes)
        {
            ObiRopeExtrudedRenderer liveRenderer = rope.liveRenderer;
            if (liveRenderer == null || liveRenderer.actor == null || !liveRenderer.actor.isLoaded)
            {
                continue;
            }

            var system = _obiSolver.GetRenderSystem<ObiRopeExtrudedRenderer>() as ObiExtrudedRopeRenderSystem;
            if (system == null)
            {
                continue;
            }

            if (rope.bakedMesh == null)
            {
                rope.bakedMesh = new Mesh { name = liveRenderer.name + " (Frozen)" };
            }

            system.BakeMesh(liveRenderer, ref rope.bakedMesh, true);

            if (rope.meshFilter == null)
            {
                rope.meshFilter = liveRenderer.gameObject.AddComponent<MeshFilter>();
            }

            if (rope.meshRenderer == null)
            {
                rope.meshRenderer = liveRenderer.gameObject.AddComponent<MeshRenderer>();
            }

            rope.meshFilter.sharedMesh = rope.bakedMesh;
            rope.meshRenderer.sharedMaterial = liveRenderer.material;
            rope.meshRenderer.enabled = true;

            liveRenderer.enabled = false;
        }

        _frozen = true;
    }

    /// <summary>
    /// Hides the baked static mesh and re-enables the live Obi renderer so it
    /// resumes being driven by the solver again.
    /// </summary>
    private void Unfreeze()
    {
        foreach (FrozenRope rope in _frozenRopes)
        {
            if (rope.meshRenderer != null)
            {
                rope.meshRenderer.enabled = false;
            }

            if (rope.liveRenderer != null)
            {
                rope.liveRenderer.enabled = true;
            }
        }

        _frozen = false;
    }

    /// <summary>
    /// Finds the camera the player is actually looking through. No camera in
    /// the scene is tagged "MainCamera", so Camera.main can't be relied on.
    /// Prefers the local player's gameplay camera; falls back to Camera.main
    /// (in case that ever changes) and finally to any enabled AudioListener's
    /// camera (the menu-over-scene camera carries one), which represents
    /// whichever view is currently the "live" one being presented.
    /// </summary>
    private static Camera ResolveActiveCamera()
    {
        if (PlayerInstance.Instance != null)
        {
            Camera playerCamera = PlayerInstance.Instance.GetCamera();
            if (playerCamera != null && playerCamera.isActiveAndEnabled)
            {
                return playerCamera;
            }
        }

        if (Camera.main != null)
        {
            return Camera.main;
        }

        AudioListener listener = FindFirstObjectByType<AudioListener>();
        if (listener != null && listener.enabled && listener.isActiveAndEnabled)
        {
            return listener.GetComponent<Camera>();
        }

        return null;
    }
}
