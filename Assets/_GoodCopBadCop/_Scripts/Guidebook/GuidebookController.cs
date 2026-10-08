using System;
using GoodCopBadCop.Input;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Opens and closes the guidebook when the local player presses Tab.
/// On open: hides the held object (locally and, via <see cref="PickableObject.RequestSetHiddenForGuidebookNetworked"/>,
/// on the server and every observer), releases its arm IK rigs, locks movement and look, and sets both
/// arm animators to the HoldingGuidebook state via PlayerAnimationController. Also hides the
/// HUD and takes over the Back button (Escape / click closes the book; the exit prompt next to it
/// shows the Tab / View key).
/// On close: reverses all of the above. The book is also put away automatically when a cutscene
/// or dialogue the player is in starts or ends, or when the first-person rig holding it is hidden
/// (intro cutscene camera / dialogue arms hide). It can't be opened while that rig is hidden.
/// The body guidebook mesh (<see cref="_bodyGuidebookObject"/>) is activated on all clients
/// via <see cref="PlayerAnimationController.SetGuidebookOpen"/> so other players can see it.
/// </summary>
[RequireComponent(typeof(PlayerAnimationController))]
[RequireComponent(typeof(PlayerPickupController))]
[RequireComponent(typeof(PlayerMovementController))]
public class GuidebookController : MonoBehaviour
{
    /// <summary>Raised whenever the local player opens the guidebook.</summary>
    public static event Action OnGuidebookOpened;

    /// <summary>Raised whenever the local player closes the guidebook.</summary>
    public static event Action OnGuidebookClosed;
    private static readonly string AnimParam  = "HoldingGuidebook";
    private static readonly string InputButton = "Guidebook";

    [SerializeField] private GameObject _guidebookObject;

    /// <summary>
    /// Guidebook mesh parented to the body rig, visible to other clients.
    /// Toggled on all clients via the networked guidebook-open state.
    /// </summary>
    [SerializeField] private GameObject _bodyGuidebookObject;

    private PlayerAnimationController _animationController;
    private PlayerPickupController    _pickupController;
    private PlayerMovementController  _movementController;

    private GameObject _deactivatedHeldObject;

    private GuidebookPageController _pageController;
    private GuidebookPageController _bodyPageController;
    private int _lastSentPage = -1;

    private PlayerInstance _playerInstance;
    private bool _wasInCutsceneOrDialogue;
    private bool _hidHud;
    private UnityEngine.Events.UnityAction _backButtonAction;

    public bool IsOpen { get; private set; }

    /// <summary>The guidebook controller of the player who most recently opened their guidebook locally.</summary>
    public static GuidebookController Local { get; private set; }

    /// <summary>Page controller on the first-person (arms) guidebook.</summary>
    public GuidebookPageController PageController => _pageController;

    private void Awake()
    {
        _animationController = GetComponent<PlayerAnimationController>();
        _pickupController    = GetComponent<PlayerPickupController>();
        _movementController  = GetComponent<PlayerMovementController>();

        if (_guidebookObject != null)
        {
            _pageController = _guidebookObject.GetComponent<GuidebookPageController>();
            _guidebookObject.SetActive(false);
        }

        if (_bodyGuidebookObject != null)
        {
            _bodyPageController = _bodyGuidebookObject.GetComponentInChildren<GuidebookPageController>(true);
            _bodyGuidebookObject.SetActive(false);
        }

        _backButtonAction = () => CloseGuidebook();
    }

    /// <summary>
    /// Closes the local player's guidebook if it is open. Call from cutscene entry paths that
    /// take over the camera without going through <see cref="PlayerInstance.SetIsInCutscene"/>
    /// (e.g. the intro cutscene), before they apply their own control lock. Also used by the
    /// emote flow — performing an emote puts the book away.
    /// Pass <paramref name="restorePlayerControl"/> = false when the caller currently owns the
    /// player control lock (see <see cref="CloseGuidebook"/>).
    /// </summary>
    public static void CloseLocalGuidebook(bool restorePlayerControl = true)
    {
        if (Local != null && Local.IsOpen) Local.CloseGuidebook(restorePlayerControl);
    }

    /// <summary>True while the local player's guidebook is open.</summary>
    public static bool IsLocalGuidebookOpen => Local != null && Local.IsOpen;

    /// <summary>
    /// False while the first-person rig holding the book is hidden — the intro cutscene disables
    /// the player's CinemachineCamera and dialogue mode disables Player_Arms. While hidden the book
    /// can't be seen, and re-enabling the rig resets the arms Animator (dropping HoldingGuidebook)
    /// while the book object stays active, so the book must not be open across that.
    /// </summary>
    private bool IsFirstPersonRigVisible
    {
        get
        {
            if (_guidebookObject == null) return true;
            Transform parent = _guidebookObject.transform.parent;
            return parent == null || parent.gameObject.activeInHierarchy;
        }
    }

    private static bool IsInCutsceneOrDialogue() =>
        ScriptedDialogueRunner.IsScriptedModeActive
        || DialogueChoiceSystem.IsInDialogueMode
        || (PlayerInstance.Instance != null && PlayerInstance.Instance.IsInCutscene);

    /// <summary>Hides the HUD and shows the Back button (Escape / click closes the book).</summary>
    private void ShowScreenUI()
    {
        UIController ui = UIController.Instance;
        if (ui == null) return;

        _hidHud = ui.IsPlayerUIVisible;
        if (_hidHud) ui.ClosePlayerUI();
        ui.ShowExclusiveBackButton(_backButtonAction);
    }

    private void RestoreScreenUI()
    {
        UIController ui = UIController.Instance;
        if (ui == null) return;

        ui.ReleaseExclusiveBackButton(_backButtonAction);
        // ShowPlayerUI itself refuses while a cutscene, dialogue or diegetic view is active.
        if (_hidHud) ui.ShowPlayerUI();
        _hidHud = false;
    }

    private void OnEnable()
    {
        _animationController.OnGuidebookOpenChanged += OnBodyGuidebookOpenChanged;
        _animationController.OnGuidebookPageChanged += OnBodyGuidebookPageChanged;

        _playerInstance = GetComponentInParent<PlayerInstance>();
        if (_playerInstance != null)
            _playerInstance.OnCutsceneStateChanged += OnCutsceneStateChanged;
    }

    private void OnDisable()
    {
        _animationController.OnGuidebookOpenChanged -= OnBodyGuidebookOpenChanged;
        _animationController.OnGuidebookPageChanged -= OnBodyGuidebookPageChanged;

        if (_playerInstance != null)
            _playerInstance.OnCutsceneStateChanged -= OnCutsceneStateChanged;

        // Never leave the shared Back button owned by a controller that can no longer close it.
        if (IsOpen) CloseGuidebook();
    }

    /// <summary>
    /// True only for the controller on the current local player object. <see cref="PlayerInstance.Instance"/>
    /// is the static local player, so checking its IsLocalPlayer alone also passed for remote players'
    /// copies and for the corpse left behind after a respawn — Tab then opened every copy, each taking
    /// the Back button, and only this player's copy was closed when a cutscene/dialogue started.
    /// </summary>
    private bool IsLocalPlayersController =>
        _playerInstance != null && _playerInstance == PlayerInstance.Instance && _playerInstance.IsLocalPlayer;

    /// <summary>
    /// Puts the book away the moment a cutscene or dialogue starts. Both entry paths
    /// (<see cref="DialogueChoiceSystem"/> and <see cref="ScriptedDialogueRunner"/>) raise this
    /// before applying their own lock / Back button, so the full close here can't undo them.
    /// Without this the dialogue camera merely hides the book while it stays "open", leaving
    /// its Back button and exit prompt on screen.
    /// </summary>
    private void OnCutsceneStateChanged(bool inCutscene)
    {
        if (!inCutscene || !IsOpen) return;
        CloseGuidebook();
    }

    private void Update()
    {
        if (!IsLocalPlayersController)
        {
            // e.g. the player object was replaced on respawn while the book was open.
            if (IsOpen) CloseGuidebook();
            return;
        }

        // Put the book away automatically when a cutscene or dialogue starts or ends
        // (start is normally handled by OnCutsceneStateChanged; this covers any path that skips it).
        bool inScene = IsInCutsceneOrDialogue();
        bool sceneChanged = _wasInCutsceneOrDialogue != inScene;
        _wasInCutsceneOrDialogue = inScene;
        if (sceneChanged && IsOpen)
        {
            CloseGuidebook();
            return;
        }

        // Safety net: the rig holding the book was hidden by something that didn't close it first.
        // Whatever hid the rig owns player control now, so don't hand it back here.
        bool rigVisible = IsFirstPersonRigVisible;
        if (IsOpen && !rigVisible)
        {
            CloseGuidebook(restorePlayerControl: false);
            return;
        }

        if (IsOpen) ReleaseHiddenItemIfNoLongerHeld();

        if (!GameSettings.Instance.GuidebookEnabled)
        {
            if (IsOpen) CloseGuidebook();
            return;
        }

        bool guidebookInput = Input.GetButtonDown(InputButton)
                              || RebindableInput.GetGamepadDown(GameAction.OpenGuidebook);

        if (!IsOpen && guidebookInput && rigVisible)
            OpenGuidebook();
        else if (IsOpen && guidebookInput)
            CloseGuidebook();

        if (IsOpen) SyncPage();
    }

    /// <summary>
    /// True while the local player is using the PC terminal or a diegetic view (tool locker,
    /// panels, etc.). Held-item zoom is excluded on purpose: Tab leaves the zoom and opens the book.
    /// </summary>
    private static bool IsBlockedByView =>
        PC.IsLocalPlayerUsingTerminal ||
        (DiegeticViewController.IsAnyViewActive && !(DiegeticViewController.Current is HeldItemZoomView));

    /// <summary>Publishes the first-person book's target page so observers flip the body copy to match.</summary>
    private void SyncPage()
    {
        if (_pageController == null) return;
        int page = _pageController.TargetLeftCount;
        if (page == _lastSentPage) return;
        _lastSentPage = page;
        _animationController.SetGuidebookPage(page);
    }

    /// <summary>
    /// Opens the guidebook: deactivates the held object, freezes the player,
    /// and transitions both animators to the HoldingGuidebook state.
    /// </summary>
    public void OpenGuidebook()
    {
        if (IsOpen || !IsLocalPlayersController || !IsFirstPersonRigVisible || IsBlockedByView) return;
        // A forced dialogue / cutscene owns the player; opening now would hide the held item
        // only for the book to be torn down again next frame.
        if (IsInCutsceneOrDialogue()) return;
        IsOpen = true;
        Local = this;

        // Enable page input before activation so the builder's OnEnable sees the local copy.
        if (_pageController != null)
            _pageController.InputEnabled = true;

        OnGuidebookOpened?.Invoke();

        // Leave held-item zoom first. Otherwise it notices the hidden item next frame and closes
        // itself, re-enabling control and the HUD while the book is open.
        if (DiegeticViewController.Current is HeldItemZoomView zoom)
            zoom.Close();

        // Deactivate held object without dropping or despawning it. The item gets a chance to
        // leave its own use / inspect mode first (e.g. exam notebook draw mode and its pages).
        if (_pickupController.HeldObject != null)
        {
            PickableObject held = _pickupController.HeldObject;

            // End any in-progress use exactly like releasing LMB (e.g. opening the book mid-mop).
            // Otherwise UsingTool stays set and the arms stay pinned in the item's use/hold states
            // (Using Mop -> Holding Mop only exits when UsingTool clears), and the deactivation
            // kills the use coroutine so nothing ever clears it.
            _pickupController.ForceStopUse();
            held.ForceClearUseState();

            held.OnHiddenForGuidebook();

            // Release the item's arm IK. Two-handed carries (supply box, package) pin both hands
            // to grip targets on the item; left on, they drag the hands (and the book in them)
            // back into the carry pose over the HoldingGuidebook animation. Networked, so
            // observers' body rigs let go too.
            SetHeldItemRigs(held.ItemData, false);

            _deactivatedHeldObject = held.gameObject;
            // Set before deactivating so self-healing paths (PlayerInventory reconcile, stowed
            // echoes) never treat this hide as a desync and re-show the item under the book.
            held.IsLocallyHiddenForGuidebook = true;
            _pickupController.IsHidingHeldItemForGuidebook = true;
            try
            {
                _deactivatedHeldObject.SetActive(false);
            }
            finally
            {
                _pickupController.IsHidingHeldItemForGuidebook = false;
            }

            // Hide it on the server and every observer as well, so it never blocks the
            // body-space guidebook other players see.
            held.RequestSetHiddenForGuidebookNetworked(true);
        }

        // Freeze movement and look, stopping any active movement immediately.
        _movementController.SetCanMove(false);
        _movementController.SetCanControl(false);
        _movementController.SetCanLook(false);

        // Both arms at full weight so the guidebook hold pose drives the full rig.
        _animationController.EnableHoldObjectTwoArmsMask();

        // Set the animator bool on both body and arms animators (networked).
        _animationController.SetAnimBool(AnimParam, true);

        if (_guidebookObject != null)
            _guidebookObject.SetActive(true);

        // Page first, so observers open the body copy straight onto the right spread.
        _lastSentPage = -1;
        SyncPage();

        // Notify all clients to show the body-space guidebook mesh.
        _animationController.SetGuidebookOpen(true);

        ShowScreenUI();
    }

    /// <summary>
    /// Closes the guidebook and restores full player state.
    /// Pass <paramref name="restorePlayerControl"/> = false when another system has already
    /// taken over the player (movement / look / control are then left as that system set them).
    /// </summary>
    public void CloseGuidebook(bool restorePlayerControl = true)
    {
        if (!IsOpen) return;
        IsOpen = false;
        OnGuidebookClosed?.Invoke();

        RestoreScreenUI();

        _animationController.SetAnimBool(AnimParam, false);

        if (_guidebookObject != null)
            _guidebookObject.SetActive(false);

        // Disable after deactivation so the builder's OnDisable still sees the local copy.
        if (_pageController != null)
            _pageController.InputEnabled = false;

        // Notify all clients to hide the body-space guidebook mesh.
        _animationController.SetGuidebookOpen(false);

        // ReferenceEquals: also clears a reference to an item destroyed while the book was open.
        if (!ReferenceEquals(_deactivatedHeldObject, null))
        {
            PickableObject pickable = RestoreHiddenHeldItem();
            bool stillHeld = pickable != null && _pickupController.HeldObject == pickable;

            if (stillHeld)
            {
                pickable.OnShownAfterGuidebook();
                SetHeldItemRigs(pickable.ItemData, true);
            }

            if (stillHeld && pickable.ItemData.usesTwoArms)
                _animationController.EnableHoldObjectTwoArmsMask();
            else if (stillHeld)
                _animationController.EnableRightArmMask();
            else
                _animationController.DisableRightArmMask();
        }
        else
        {
            // Nothing held — clear all arm layers.
            _animationController.DisableRightArmMask();
        }

        if (!restorePlayerControl) return;

        // Restore look first so SetCanControl finds CanLook == true and re-enables the reticle.
        _movementController.SetCanLook(true);
        _movementController.SetCanControl(true);
        _movementController.SetCanMove(true);
    }

    /// <summary>
    /// Re-activates the item hidden when the book opened, clears its local hidden flag and
    /// un-hides it on the server and observers (always allowed, even if it left the hand).
    /// Returns the item's <see cref="PickableObject"/> (null if it was destroyed or has none).
    /// </summary>
    private PickableObject RestoreHiddenHeldItem()
    {
        GameObject hidden = _deactivatedHeldObject;
        _deactivatedHeldObject = null;
        if (hidden == null) return null;

        PickableObject pickable = hidden.GetComponent<PickableObject>();
        if (pickable != null)
            pickable.IsLocallyHiddenForGuidebook = false;

        hidden.SetActive(true);

        if (pickable != null)
            pickable.RequestSetHiddenForGuidebookNetworked(false);

        return pickable;
    }

    /// <summary>
    /// The hidden item left the hand while the book stayed open (death drop, forced release,
    /// despawn). Show it again right away instead of leaving it invisible until the book closes,
    /// and release its arm IK so a carry pose doesn't come back on close.
    /// </summary>
    private void ReleaseHiddenItemIfNoLongerHeld()
    {
        if (ReferenceEquals(_deactivatedHeldObject, null)) return;

        PickableObject pickable = _deactivatedHeldObject != null
            ? _deactivatedHeldObject.GetComponent<PickableObject>()
            : null;
        if (pickable != null && _pickupController.HeldObject == pickable) return;

        RestoreHiddenHeldItem();
    }

    /// <summary>
    /// Turns the held item's arm IK / aim rigs off while the book is open, or back on (to the
    /// same weights <see cref="PlayerPickupController.PickUpObject"/> uses) when it closes.
    /// The IK targets themselves are left assigned, so restoring only needs the weights.
    /// </summary>
    private void SetHeldItemRigs(PickableItemData itemData, bool active)
    {
        if (itemData == null) return;
        float weight = active ? 1f : 0f;
        const float blendTime = 0.2f;

        if (itemData.useRightIK) _animationController.SetRightArmRigWeightSmooth(weight, blendTime);
        if (itemData.useLeftIK)  _animationController.SetLeftArmRigWeightSmooth(weight, blendTime);
        if (itemData.useAimIK)   _animationController.SetAimRigWeightSmooth(weight, blendTime);
    }

    /// <summary>
    /// Callback fired on all clients when the networked guidebook-open state changes.
    /// Toggles the body-space guidebook mesh only for clients observing this player —
    /// skipped for the owner so the holding player never sees their own body guidebook.
    /// </summary>
    private void OnBodyGuidebookOpenChanged(bool isOpen)
    {
        if (_bodyGuidebookObject == null || _animationController.IsOwner) return;
        _bodyGuidebookObject.SetActive(isOpen);

        // The builder rebuilt the book in OnEnable; lay it out on the owner's current spread.
        if (isOpen && _bodyPageController != null)
            _bodyPageController.SnapTo(_animationController.GuidebookPage);
    }

    /// <summary>
    /// Fired on all clients when the owner turns a page. Observers play the same flips on the
    /// body-space guidebook; the owner already sees its own first-person copy.
    /// </summary>
    private void OnBodyGuidebookPageChanged(int page)
    {
        if (_bodyPageController == null || _animationController.IsOwner) return;
        if (!_bodyGuidebookObject.activeInHierarchy) return; // snapped on open instead
        _bodyPageController.TurnTo(page);
    }
}
