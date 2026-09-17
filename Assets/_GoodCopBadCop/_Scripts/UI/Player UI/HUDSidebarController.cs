using TMPro;
using UnityEngine;

/// <summary>
/// Consolidated top-right HUD sidebar. Merges what used to be three separate widgets — the
/// "CURRENT ORDERS" checklist (<see cref="TutorialObjectiveList"/>), the checkpoint maintenance
/// readout (<see cref="CheckpointMaintenanceHUD"/>), and an "OPTIONAL" side-task list
/// (<see cref="OptionalTaskSection"/>) — into one panel that expands/collapses on TAB.
///
/// Expanded: the full "TASKS" panel (current orders, maintenance progress, optional tasks) is
/// shown beneath the always-visible checkpoint integrity header.
/// Collapsed: only the checkpoint integrity header remains, plus a small "TAB" hint and a
/// badge showing the total number of incomplete tasks across all three sections.
/// </summary>
public class HUDSidebarController : MonoBehaviour
{
    private static HUDSidebarController _instance;

    public static HUDSidebarController Instance
    {
        get
        {
            if (_instance == null)
                _instance = FindFirstObjectByType<HUDSidebarController>(FindObjectsInactive.Include);
            return _instance;
        }
        private set => _instance = value;
    }

    [Header("Toggle")]
    [SerializeField] private KeyCode toggleKey = KeyCode.Tab;
    [SerializeField] private bool startExpanded;

    [Header("Panels")]
    [Tooltip("The full TASKS panel shown while expanded.")]
    [SerializeField] private GameObject tasksPanelRoot;
    [Tooltip("The compact TAB hint + badge shown while collapsed.")]
    [SerializeField] private GameObject collapsedHintRoot;

    [Header("Collapsed Badge")]
    [SerializeField] private GameObject badgeRoot;
    [SerializeField] private TextMeshProUGUI badgeCountText;

    [Header("Sections")]
    [SerializeField] private TutorialObjectiveList currentOrders;
    [SerializeField] private CheckpointMaintenanceHUD maintenance;
    [SerializeField] private OptionalTaskSection optionalTasks;

    private bool _isExpanded;

    private void Awake()
    {
        Instance = this;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    private void Start()
    {
        SetExpanded(startExpanded);
    }

    private void Update()
    {
        if (Input.GetKeyDown(toggleKey))
            SetExpanded(!_isExpanded);
    }

    public void SetExpanded(bool expanded)
    {
        _isExpanded = expanded;

        if (tasksPanelRoot != null) tasksPanelRoot.SetActive(expanded);
        if (collapsedHintRoot != null) collapsedHintRoot.SetActive(!expanded);

        RefreshBadge();
    }

    /// <summary>Recomputes the collapsed-state incomplete-task badge from all three sections.</summary>
    public void RefreshBadge()
    {
        if (badgeRoot == null && badgeCountText == null) return;

        int count = 0;
        if (currentOrders != null) count += currentOrders.IncompleteCount;
        if (maintenance != null) count += maintenance.IncompleteCount;
        if (optionalTasks != null) count += optionalTasks.IncompleteCount;

        if (badgeCountText != null)
            badgeCountText.text = count.ToString();

        if (badgeRoot != null)
            badgeRoot.SetActive(!_isExpanded && count > 0);
    }
}
