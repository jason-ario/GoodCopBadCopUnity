using UnityEngine;

/// <summary>
/// Plays a creepy laugh at a random position around the kicking player.
/// Each cycle picks a random kick target between minKicksToTrigger and maxKicksToTrigger.
/// After laughsBeforeCooldown laughs the long cooldown starts; once it expires everything resets.
/// Local client only — no networking needed.
/// </summary>
public class CreepyKickReaction : MonoBehaviour
{
    [SerializeField] private AudioClip[] laughClips;
    [SerializeField] private int minKicksToTrigger = 2;
    [SerializeField] private int maxKicksToTrigger = 8;
    [SerializeField] private int laughsBeforeCooldown = 3;
    [SerializeField] private float cooldownMinutes = 3f;
    [SerializeField] private float minSpawnDistance = 3f;
    [SerializeField] private float maxSpawnDistance = 8f;
    [SerializeField] [Range(0f, 1f)] private float volume = 1f;

    private int _kickCount = 0;
    private int _kickTarget = 0;
    private int _laughCount = 0;
    private float _cooldownUntil = float.MinValue;

    private void Awake()
    {
        _kickTarget = RandomKickTarget();
    }

    public void OnKick(Vector3 playerPosition)
    {
        if (laughClips == null || laughClips.Length == 0) return;

        if (Time.time < _cooldownUntil) return;

        _kickCount++;
        if (_kickCount < _kickTarget) return;

        // Threshold reached — play a laugh and pick a new target
        _kickCount = 0;
        _kickTarget = RandomKickTarget();
        _laughCount++;

        AudioClip clip = laughClips[Random.Range(0, laughClips.Length)];
        AudioSource.PlayClipAtPoint(clip, RandomPositionAround(playerPosition), volume);

        if (_laughCount >= laughsBeforeCooldown)
        {
            _laughCount = 0;
            _cooldownUntil = Time.time + cooldownMinutes * 60f;
        }
    }

    private int RandomKickTarget() => Random.Range(minKicksToTrigger, maxKicksToTrigger + 1);

    private Vector3 RandomPositionAround(Vector3 origin)
    {
        float angle = Random.Range(0f, Mathf.PI * 2f);
        float distance = Random.Range(minSpawnDistance, maxSpawnDistance);
        float height = Random.Range(0.5f, 2f);

        return origin + new Vector3(
            Mathf.Cos(angle) * distance,
            height,
            Mathf.Sin(angle) * distance
        );
    }
}
