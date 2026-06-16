namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Helpers;

/// <summary>
/// Central home for every piece of notification copy.
///
/// Tone guide: warm, supportive and personal — we speak directly to the carer
/// and always name the pet. Notifications should feel like a gentle nudge from
/// a friend, never a robotic system alert. Keep one emoji at most so the
/// message stays calm.
///
/// All copy is pulled from the localized resources via <see cref="LocalizationManager"/>,
/// so a notification is rendered in whatever language the user has chosen.
/// Placeholders ({0} = pet, {1} = medication/what, etc.) are filled at send time.
/// </summary>
public static class NotificationMessages
{
    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>Title shown on a medication reminder, e.g. "Time for Bella's medication ❤️".</summary>
    public static string MedicationTitle(string petName)
        => L.Format("Notif_MedicationTitle", SafePet(petName));

    /// <summary>
    /// Body for a medication reminder. The phrasing gently rotates by
    /// <paramref name="slot"/> so a pet on several daily doses doesn't see the
    /// exact same sentence every time.
    /// </summary>
    public static string MedicationBody(string petName, string medicationName, int slot)
    {
        var pet = SafePet(petName);
        var med = SafeMed(medicationName);

        var keys = new[]
        {
            "Notif_MedicationBody0",
            "Notif_MedicationBody1",
            "Notif_MedicationBody2",
            "Notif_MedicationBody3",
            "Notif_MedicationBody4",
            "Notif_MedicationBody5"
        };

        return L.Format(keys[Math.Abs(slot) % keys.Length], pet, med);
    }

    /// <summary>Title for a catch-up reminder about dose(s) missed while the device was unavailable.</summary>
    public static string MedicationMissedTitle(string petName)
        => L.Format("Notif_MissedTitle", SafePet(petName));

    /// <summary>
    /// Body for a missed-dose catch-up. Kept gentle and reassuring — the goal is
    /// to surface a missed medication without alarming the carer.
    /// </summary>
    public static string MedicationMissedBody(string petName, string medicationName, int count)
    {
        var pet = SafePet(petName);
        var med = SafeMed(medicationName);

        return count <= 1
            ? L.Format("Notif_MissedBodyOne", pet, med)
            : L.Format("Notif_MissedBodyMany", pet, count, med);
    }

    // ── Reserved for future reminder types ───────────────────────────────

    public static string MoodCheckInTitle(string petName) => L.Format("Notif_MoodCheckInTitle", SafePet(petName));
    public static string MoodCheckInBody(string petName) => L.Format("Notif_MoodCheckInBody", SafePet(petName));

    public static string WeightCheckInTitle(string petName) => L.Format("Notif_WeightCheckInTitle", SafePet(petName));
    public static string WeightCheckInBody(string petName) => L.Format("Notif_WeightCheckInBody", SafePet(petName));

    public static string AppointmentTitle(string petName) => L.Format("Notif_AppointmentTitle", SafePet(petName));
    public static string AppointmentBody(string petName, string what) => L.Format("Notif_AppointmentBody", SafePet(petName), what);

    private static string SafePet(string petName)
        => string.IsNullOrWhiteSpace(petName) ? L.GetString("Notif_SafePet") : petName.Trim();

    private static string SafeMed(string medicationName)
        => string.IsNullOrWhiteSpace(medicationName) ? L.GetString("Notif_DefaultMedication") : medicationName.Trim();
}
