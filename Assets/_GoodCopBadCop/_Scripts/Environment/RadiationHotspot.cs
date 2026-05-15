using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Trigger zone that increases the player's radiation accumulation rate
/// while they remain inside it. Attach to a GameObject with a Trigger Collider.
/// </summary>
[RequireComponent(typeof(Collider))]
public class RadiationHotspot : MonoBehaviour
{
    [Header("Hotspot Settings")]
    [Tooltip("Additional radiation per second added on top of the player's passive rate.")]
    [SerializeField] private float bonusRadiationPerSecond = 0.5f;

    [Header("Events")]
    public UnityEvent OnPlayerEnter;
    public UnityEvent OnPlayerExit;

    private PlayerRadiation _playerRadiation;
    private bool _playerInside;

    private void Awake()
    {
        Collider col = GetComponent<Collider>();
        col.isTrigger = true;
    }

    private void Update()
    {
        if (!_playerInside || _playerRadiation == null) return;

        _playerRadiation.AddRadiation(bonusRadiationPerSecond * Time.deltaTime);
    }

    private void OnTriggerEnter(Collider other)
    {
        PlayerRadiation radiation = other.GetComponentInParent<PlayerRadiation>();
        if (radiation == null) return;

        _playerRadiation = radiation;
        _playerInside = true;
        OnPlayerEnter?.Invoke();

        Debug.Log($"[RadiationHotspot] Player entered {gameObject.name}. Bonus rate: +{bonusRadiationPerSecond}/s");
    }

    private void OnTriggerExit(Collider other)
    {
        if (_playerRadiation == null) return;

        PlayerRadiation radiation = other.GetComponentInParent<PlayerRadiation>();
        if (radiation != _playerRadiation) return;

        _playerRadiation = null;
        _playerInside = false;
        OnPlayerExit?.Invoke();

        Debug.Log($"[RadiationHotspot] Player exited {gameObject.name}.");
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0f, 1f, 0f, 0.15f);
        Collider col = GetComponent<Collider>();
        if (col is BoxCollider box)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(box.center, box.size);
            Gizmos.color = new Color(0f, 1f, 0f, 0.6f);
            Gizmos.DrawWireCube(box.center, box.size);
        }
        else if (col is SphereCollider sphere)
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawSphere(sphere.center, sphere.radius);
            Gizmos.color = new Color(0f, 1f, 0f, 0.6f);
            Gizmos.DrawWireSphere(sphere.center, sphere.radius);
        }
        Gizmos.matrix = Matrix4x4.identity;
    }
#endif
}
