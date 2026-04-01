using UnityEngine;
using UnityEngine.Events;

public class ShiftClockController : MonoBehaviour
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

    public bool ShiftEnded => shiftEnded;
    public float CurrentTimeInHours => currentTimeMinutes / 60f;
    public int CurrentHour24 => Mathf.FloorToInt(currentTimeMinutes / 60f) % 24;
    public int CurrentMinute => Mathf.FloorToInt(currentTimeMinutes) % 60;

    private float currentTimeMinutes;
    private float endTimeMinutes;
    private bool shiftEnded;

    private void Start()
    {
        SetTime(startHour, startMinute);
        endTimeMinutes = endHour * 60f;

        UpdateClockVisuals();
        CheckImmediateShiftEnd();
    }

    private void Update()
    {
        if (shiftEnded)
            return;

        float previousTime = currentTimeMinutes;
        currentTimeMinutes += gameMinutesPerSecond * Time.deltaTime;

        UpdateClockVisuals();

        if (previousTime < endTimeMinutes && currentTimeMinutes >= endTimeMinutes)
        {
            currentTimeMinutes = endTimeMinutes;
            UpdateClockVisuals();

            shiftEnded = true;
            onShiftEnd?.Invoke();
        }
    }

    public void SetTime(int hour24, int minute)
    {
        hour24 = Mathf.Clamp(hour24, 0, 23);
        minute = Mathf.Clamp(minute, 0, 59);

        currentTimeMinutes = hour24 * 60f + minute;
        UpdateClockVisuals();
    }

    public void SetTime(float totalMinutes)
    {
        currentTimeMinutes = Mathf.Max(0f, totalMinutes);
        UpdateClockVisuals();
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
        shiftEnded = true;
    }

    public void RestartShift()
    {
        shiftEnded = false;
        currentTimeMinutes = startHour * 60f + startMinute;
        endTimeMinutes = endHour * 60f;
        UpdateClockVisuals();
        CheckImmediateShiftEnd();
    }

    private void CheckImmediateShiftEnd()
    {
        if (currentTimeMinutes >= endTimeMinutes)
        {
            shiftEnded = true;
            onShiftEnd?.Invoke();
        }
    }

    private void UpdateClockVisuals()
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