using System;
using UnityEngine;
using UnityEngine.Events;

public class TimeSystem : MonoBehaviour
{
    public static TimeSystem Instance;
    
    [Header("Time Settings")]
    [SerializeField] private float timeSpeedMultiplier = 60f;

    [Header("Events")]
    public UnityEvent onDayEnd;

    private const int DayStartHour = 8;
    private const int DayEndHour = 20;

    private float _currentTimeInSeconds;
    private bool _isDayOver;

    private float DayDurationInSeconds => (DayEndHour - DayStartHour) * 3600f;

    public string FormattedTime
    {
        get
        {
            int totalMinutes = Mathf.FloorToInt(_currentTimeInSeconds / 60f);
            int hours = DayStartHour + totalMinutes / 60;
            int minutes = totalMinutes % 60;
            return $"{hours:D2}:{minutes:D2}";
        }
    }

    /// <summary>Returns day progress from 0.0 (8am) to 1.0 (8pm).</summary>
    public float DayProgress => Mathf.Clamp01(_currentTimeInSeconds / (DayDurationInSeconds / timeSpeedMultiplier));

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        _currentTimeInSeconds = 0f;
        _isDayOver = false;
    }

    private void Update()
    {
        if (_isDayOver) return;

        _currentTimeInSeconds += Time.deltaTime * timeSpeedMultiplier;

        float realDayDuration = DayDurationInSeconds / timeSpeedMultiplier;
        if (_currentTimeInSeconds >= realDayDuration)
        {
            _currentTimeInSeconds = realDayDuration;
            _isDayOver = true;
            onDayEnd?.Invoke();
        }
    }
}
