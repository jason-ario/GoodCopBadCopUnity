using System;

/// <summary>
/// Diegetic view for the wall task page. Extends <see cref="DiegeticViewController"/>.
/// No custom interaction — the player simply looks at the task list.
/// Fires <see cref="OnTaskPageViewed"/> once on the first open; use this to complete
/// the "Look at the task list" tutorial objective in Day_01.
/// </summary>
public class TaskPageDiegeticController : DiegeticViewController
{
    /// <summary>
    /// Fired locally the first time the task page view is opened by any player.
    /// Subscribe in Day_01 to complete the tutorial objective.
    /// </summary>
    public static event Action OnTaskPageViewed;

    private bool _viewedOnce;

    protected override void OnOpened()
    {
        if (_viewedOnce) return;
        _viewedOnce = true;
        OnTaskPageViewed?.Invoke();
    }
}
