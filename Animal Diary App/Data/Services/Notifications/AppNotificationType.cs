namespace Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// Categories of reminders the app can raise. Each type maps to its own
/// reserved range of notification IDs (see <see cref="NotificationIds"/>) and
/// its own warm copy (see <see cref="NotificationMessages"/>).
///
/// Adding a new reminder kind (appointments, weigh-ins, mood check-ins …) is a
/// matter of adding a value here plus a matching ID helper and message helper.
/// </summary>
public enum AppNotificationType
{
    MedicationReminder,
    MoodCheckIn,
    WeightCheckIn,
    Appointment
}
