using System.Collections.Generic;
using UnityEngine;
using Obi;

/// <summary>
/// Keeps an ObiSolver's simulation disabled by default, and only enables it
/// while the item is actively being held (by any player). This is the common
/// case for held tools like the Mop: the rope only needs to actually simulate
/// while it's in a player's hands - at all other times (dropped, thrown,
/// sitting somewhere) it should stay disabled to avoid paying the (often
/// expensive) per-frame Obi solver cost.
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
    [Header("Held Settings")]
    [Tooltip("The pickable item this solver belongs to. While it's held (by anyone), the " +
             "solver runs live; otherwise it stays disabled (mesh baked). If unset, will " +
             "search parents for one.")]
    [SerializeField] private PickableObject pickableObject;

    [Header("Settle Settings (after drop/throw)")]
    [Tooltip("Rigidbody to check for rest state. If unset, will search parents for one.")]
    [SerializeField] private Rigidbody rigidbodyToCheck;

    [Tooltip("How long (in seconds) to keep the solver running after the item stops being " +
             "held, so the rope can visually settle before being frozen. The solver freezes " +
             "sooner than this if the Rigidbody falls asleep first.")]
    [SerializeField] private float settleDuration = 2f;

    private ObiSolver _obiSolver;
    private bool _frozen;
    private bool _wasHeld;
    private float _settleTimer;
    private bool _settling;
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
            // Always keep the solver live while held, regardless of distance -
            // this may be held by another player whose own view depends on it
            // simulating correctly, even if it reads as far away from our camera.
            SetSolverEnabled(true);
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
}
