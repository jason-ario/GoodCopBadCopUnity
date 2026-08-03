using UnityEngine;

/// <summary>
/// ScriptableObject preset that defines a single "mutant breach" event — who spawns, how many,
/// and the timing/messaging around the alarm. Create one asset per breach variant (e.g. a small
/// scout breach vs. a large horde breach) and assign the presets a day is allowed to roll from
/// to <see cref="DayBase.PossibleBreaches"/>.
/// Create via: Assets > Create > GoodCopBadCop > Enemy > Mutant Breach Data
/// </summary>
[CreateAssetMenu(menuName = "GoodCopBadCop/Enemy/Mutant Breach Data", fileName = "NewMutantBreachData")]
public class MutantBreachData : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Display name for this breach preset. Used only for editor/debug clarity.")]
    public string breachName;

    [Header("Enemies")]
    [Tooltip("Networked mutant prefabs this breach can spawn from, picked at random per spawn. " +
             "Each must contain a MutantEnemy component and be registered in NetworkManager's prefab list.")]
    public GameObject[] mutantPrefabs;

    [Tooltip("Fixed total number of mutants spawned when this breach triggers. Includes any " +
             "uniqueMutantPrefabs — e.g. mutantCount 4 with 1 unique entry spawns that unique " +
             "mutant plus 3 random picks from mutantPrefabs.")]
    [Min(1)]
    public int mutantCount = 4;

    [Tooltip("Optional prefabs guaranteed to spawn exactly once each per breach — e.g. a singular " +
             "boss/unique mutant mixed in with the random pool. Each must contain a MutantEnemy " +
             "component and be registered in NetworkManager's prefab list. Spawn order among the " +
             "full mutantCount is shuffled, so unique mutants don't always appear first. If the " +
             "number of unique prefabs exceeds mutantCount, all unique prefabs still spawn and the " +
             "cap is exceeded for them (no random picks are added).")]
    public GameObject[] uniqueMutantPrefabs;

    [Header("Timing")]
    [Tooltip("Seconds between the alarm/notification starting and the first mutant spawning. " +
             "Gives players a moment to ready themselves after the warning.")]
    [Min(0f)]
    public float alarmLeadTimeSeconds = 5f;

    [Tooltip("Seconds between each individual mutant spawn once spawning begins.")]
    [Min(0f)]
    public float spawnStaggerSeconds = 0.4f;

    [Header("Notification")]
    [Tooltip("Message shown via PlayerTutorialUI when the breach is detected.")]
    [TextArea]
    public string notificationMessage = "A mutant breach has been detected. Ready yourself for combat.";

    [Tooltip("Seconds the notification text stays on screen.")]
    [Min(0.5f)]
    public float notificationHoldDuration = 4f;

    [Header("Aggro")]
    [Tooltip("When true, every mutant spawned by this breach is forced into aggro mode toward " +
             "MutantBreachManager's configured aggro target, regardless of MutantEnemyData.aggroChance.")]
    public bool forceAggro = true;

    [Header("Campaign Finale")]
    [Tooltip("When true, the moment any mutant spawned by this breach begins fleeing instead of " +
             "dying (see MutantEnemy.fleeInsteadOfDie), the campaign is marked complete and the " +
             "Thanks For Playing screen is shown on all clients. Use only on the demo's final " +
             "scripted breach (e.g. the Day 5 Ocho breach) — leave false on every other breach.")]
    public bool showThanksForPlayingOnFlee = false;

    [Tooltip("When true, the moment every mutant spawned by this breach has been resolved " +
             "(defeated or fled) and the breach's alarm/music has stopped, the campaign is " +
             "marked complete and the Thanks For Playing screen is shown on all clients. Use " +
             "only on the demo's final scripted breach (e.g. the Day 3 finale breach) — leave " +
             "false on every other breach.")]
    public bool showThanksForPlayingOnClear = false;
}
