// SPDX-License-Identifier: AGPL-3.0-or-later
namespace Monitor.Application.Presentation;

public enum AlarmPlaybackMode { Continuous, Notifications }
public enum AlarmSoundMode { Inherit, SingleGroup, Continuous }
public enum AlarmSoundDuration { SingleGroup, Continuous }

// Saved configuration keeps the reminder interval when reminders are disabled.
public sealed record AlarmNotificationSettings(int RepeatSuppressionMilliseconds, bool ReminderEnabled, int ReminderMilliseconds)
{
    public AlarmSoundMode SoundMode { get; init; }
    public static AlarmNotificationSettings Default { get; } = new(0, false, 30000);

    public void Validate()
    {
        if (!Enum.IsDefined(SoundMode) || RepeatSuppressionMilliseconds is < 0 or > AlarmNotificationPolicy.MaximumMilliseconds ||
            ReminderMilliseconds is < 1 or > AlarmNotificationPolicy.MaximumMilliseconds)
        { throw new ArgumentException("AlarmNotification.InvalidSettings"); }
    }

    public AlarmNotificationPolicy ToPolicy(AlarmPlaybackMode defaultMode = AlarmPlaybackMode.Notifications)
    {
        Validate();
        if (!Enum.IsDefined(defaultMode)) { throw new ArgumentException("AlarmNotification.InvalidDefaultMode", nameof(defaultMode)); }
        bool continuous = SoundMode == AlarmSoundMode.Continuous || SoundMode == AlarmSoundMode.Inherit && defaultMode == AlarmPlaybackMode.Continuous;
        return continuous ? new(0, 0) { SoundDuration = AlarmSoundDuration.Continuous }
            : new(RepeatSuppressionMilliseconds, ReminderEnabled ? ReminderMilliseconds : 0);
    }
}
