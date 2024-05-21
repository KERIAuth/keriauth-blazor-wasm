using System;
using KeriAuth.BrowserExtension.Services;

namespace KeriAuth.BrowserExtension.Helper;

public class IdleAlarm
{
    private readonly IAlarmService _timer;
    private readonly string _timerName = nameof(IdleAlarm);
    private readonly TimeSpan _debounceTimeSpan = TimeSpan.FromSeconds(AppConfig.IdleDebounceTimeSpanSecs);
    private readonly TimeSpan _timeoutTimeSpan = TimeSpan.FromSeconds(AppConfig.IdleTimeoutTimeSpanSecs);
    private readonly Action _action;

    public IdleAlarm(IAlarmService timer, Action action)
    {
        _timer = timer;
        _action = action;
        _timer.CreateAlarm(_timerName, _action, _timeoutTimeSpan);
        _timer.StartAlarm(_timerName);
    }

    public void Reset()
    {
        _timer.ResetAlarm(_timerName, _timeoutTimeSpan, _debounceTimeSpan);
    }

    public void Stop()
    {
        _timer.StopAlarm(_timerName);
    }   
}
