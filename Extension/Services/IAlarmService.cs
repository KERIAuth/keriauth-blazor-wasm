namespace Extension.Services;
// The debounceReset method is added to the ITimer interface and implemented in either StandardTimer and ChromeExtensionTimer. 
// It ensures that the timer is only reset after a specified delay, reducing the frequency of resets during continuous events like mouse movements or key presses.
// The usage with event listeners demonstrates how to set up a debounced reset of the timer when the user interacts with the application.
// This approach ensures that the timer is not reset too frequently, which can be particularly useful in scenarios where events like onmousemove or onkeydown are fired rapidly.

using System;

public interface IAlarmService
{
    void CreateAlarm(string name, Action callback, TimeSpan delay);
    void StartAlarm(string name);
    void StopAlarm(string name);
    void ResetAlarm(string name, TimeSpan newDuration, TimeSpan debounceDuration);
}
