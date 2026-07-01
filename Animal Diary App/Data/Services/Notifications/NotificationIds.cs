namespace Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// Deterministic notification-ID scheme.
///
/// Plugin.LocalNotification identifies every scheduled notification by a single
/// <c>int</c>. To schedule, update and cancel reliably we need the ID for a
/// given reminder to be reproducible from its owning entity — never random.
///
/// Each notification type owns a numeric range so IDs can never collide across
/// types. Within a range every entity (e.g. a medication) gets a block of
/// <see cref="SlotsPerEntity"/> consecutive IDs so it can have several reminder
/// times per day.
///
///   medication 7, reminder slot 2  ->  1_000_000 + 7*10 + 2  =  1_000_072
/// </summary>
public static class NotificationIds
{
    /// <summary>Maximum reminder slots reserved per entity. Keep in sync with the UI cap.</summary>
    public const int SlotsPerEntity = 10;

    private const int MedicationBase = 1_000_000;
    private const int MoodCheckInBase = 2_000_000;
    private const int WeightCheckInBase = 3_000_000;
    private const int AppointmentBase = 4_000_000;

    // Instance-based scheduling: each materialized ReminderInstance gets a unique
    // OS notification id derived from its database id. Missed-dose catch-up
    // notifications get their own range keyed by medication id.
    private const int InstanceBase = 100_000_000;
    private const int MissedDoseBase = 200_000_000;

    /// <summary>ID for the <paramref name="slot"/>-th daily reminder of a medication.</summary>
    public static int MedicationReminder(int medicationId, int slot)
        => MedicationBase + medicationId * SlotsPerEntity + slot;

    /// <summary>Every notification ID belonging to a medication (used when cancelling).</summary>
    public static IEnumerable<int> AllMedicationReminders(int medicationId)
    {
        for (var slot = 0; slot < SlotsPerEntity; slot++)
            yield return MedicationReminder(medicationId, slot);
    }

    /// <summary>Unique OS notification id for a materialized reminder instance.</summary>
    public static int ForInstance(int instanceId) => InstanceBase + instanceId;

    /// <summary>Notification id for a medication's coalesced "missed dose" catch-up.</summary>
    public static int MissedDose(int medicationId) => MissedDoseBase + medicationId;

    // ── Reserved for future reminder types ───────────────────────────────
    public static int MoodCheckIn(int petId) => MoodCheckInBase + petId * SlotsPerEntity;
    public static int WeightCheckIn(int petId) => WeightCheckInBase + petId * SlotsPerEntity;
    public static int Appointment(int appointmentId) => AppointmentBase + appointmentId * SlotsPerEntity;
}
