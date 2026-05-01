using Unity.Netcode;
using UnityEngine;
using UnityEngine.Events;

public class ShiftClockController : NetworkBehaviour
{
    [Header("Clock Hands")]
    [SerializeField] private Transform hourHand;
    [SerializeField] private Transform minuteHand;

    [Header("Time Settings")]
    [Tooltip("Starting hour in 24h format. Example: 8 = 8:00 AM, 17 = 5:00 PM")]
    [Range(0, 23)]
    [SerializeField] private int startHour = 8;

    [Tooltip("Starting minute")]
    [Range(0, 59)]
    [SerializeField] private int startMinute = 0;

    [Tooltip("Shift end hour in 24h format. Example: 17 = 5:00 PM")]
    [Range(0, 23)]
    [SerializeField] private int endHour = 17;

    [Tooltip("How many in-game minutes pass per real second")]
    [SerializeField] private float gameMinutesPerSecond = 10f;

    [Header("Rotation Settings")]
    [Tooltip("Use this if your clock hands rotate backwards")]
    [SerializeField] private bool invertRotation = false;

    [Tooltip("Extra rotation offset if your hand art points somewhere other than 12 o'clock at 0 degrees")]
    [SerializeField] private float hourHandRotationOffset = 0f;

    [Tooltip("Extra rotation offset if your hand art points somewhere other than 12 o'clock at 0 degrees")]
    [SerializeField] private float minuteHandRotationOffset = 0f;

    [Header("Events")]
    public UnityEvent onShiftEnd;

    // Server-authoritative time synced to all clients
    private NetworkVariable<float> _networkTimeMinutes = new NetworkVariable<float>(
        0f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool ShiftEnded => _shiftEnded;
    public float CurrentTimeInHours => _networkTimeMinutes.Value / 60f;
    public int CurrentHour24 => Mathf.FloorToInt(_networkTimeMinutes.Value / 60f) % 24;
    public int CurrentMinute => Mathf.FloorToInt(_networkTimeMinutes.Value) % 60;

    private float _endTimeMinutes;
    private bool _shiftEnded;

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();
        _endTimeMinutes = endHour * 60f;

        if (IsServer)
        {
            _networkTimeMinutes.Value = startHour * 60f + startMinute;
        }

        _networkTimeMinutes.OnValueChanged += OnTimeChanged;

        UpdateClockVisuals(_networkTimeMinutes.Value);
        CheckImmediateShiftEnd();
    }

    public override void OnNetworkDespawn()
    {
        _networkTimeMinutes.OnValueChanged -= OnTimeChanged;
    }

    private void OnTimeChanged(float oldValue, float newValue)
    {
        UpdateClockVisuals(newValue);
    }

    private void Update()
    {
        if (!IsServer) return;
        if (_shiftEnded) return;

        float previousTime = _networkTimeMinutes.Value;
        _networkTimeMinutes.Value += gameMinutesPerSecond * Time.deltaTime;

        if (previousTime < _endTimeMinutes && _networkTimeMinutes.Value >= _endTimeMinutes)
        {
            _networkTimeMinutes.Value = _endTimeMinutes;
            _shiftEnded = true;
            TriggerShiftEndClientRpc();
        }
    }

    [ClientRpc]
    private void TriggerShiftEndClientRpc()
    {
        _shiftEnded = true;
        onShiftEnd?.Invoke();
    }

    public void SetTime(int hour24, int minute)
    {
        if (!IsServer) return;
        hour24 = Mathf.Clamp(hour24, 0, 23);
        minute = Mathf.Clamp(minute, 0, 59);
        _networkTimeMinutes.Value = hour24 * 60f + minute;
    }

    public void SetTime(float totalMinutes)
    {
        if (!IsServer) return;
        _networkTimeMinutes.Value = Mathf.Max(0f, totalMinutes);
    }

    public void PauseClock()
    {
        enabled = false;
    }

    public void ResumeClock()
    {
        enabled = true;
    }

    public void StopClock()
    {
        _shiftEnded = true;
    }

    public void RestartShift()
    {
        _shiftEnded = false;
        if (IsServer)
        {
            _networkTimeMinutes.Value = startHour * 60f + startMinute;
            _endTimeMinutes = endHour * 60f;
        }
        UpdateClockVisuals(_networkTimeMinutes.Value);
        CheckImmediateShiftEnd();
    }

    private void CheckImmediateShiftEnd()
    {
        if (_networkTimeMinutes.Value >= _endTimeMinutes)
        {
            _shiftEnded = true;
            onShiftEnd?.Invoke();
        }
    }

    private void UpdateClockVisuals(float currentTimeMinutes)
    {
        float minutesOn12HourClock = currentTimeMinutes % 720f;
        float minuteInHour = currentTimeMinutes % 60f;

        // 360 degrees / 60 minutes = 6 degrees per minute
        float minuteAngle = minuteInHour * 6f;

        // 360 degrees / 12 hours = 30 degrees per hour
        // Add fractional movement from minutes so hour hand moves smoothly
        float hourAngle = minutesOn12HourClock * 0.5f;

        if (!invertRotation)
        {
            minuteAngle = -minuteAngle;
            hourAngle = -hourAngle;
        }

        if (minuteHand != null)
            minuteHand.localRotation = Quaternion.Euler(0f, 0f, minuteAngle + minuteHandRotationOffset);

        if (hourHand != null)
            hourHand.localRotation = Quaternion.Euler(0f, 0f, hourAngle + hourHandRotationOffset);
    }
}