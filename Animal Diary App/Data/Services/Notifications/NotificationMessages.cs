namespace Animal_Diary_App.Data.Services.Notifications;

using Animal_Diary_App.Helpers;

/// <summary>
/// Central home for every piece of notification copy.
///
/// Tone guide (see AI/app-voice.md §8): a notification states the fact and nothing
/// else. Pet name, what, when. No emoji, no exclamation points, no urgency or alarm
/// words ("overdue", "don't forget"), no guilt, no evaluation of a health number,
/// and never a re-engagement ping. A reminder is not an emergency.
///
/// All copy is pulled from the localized resources via <see cref="LocalizationManager"/>,
/// so a notification is rendered in whatever language the user has chosen.
/// Placeholders ({0} = pet, {1} = medication/what, etc.) are filled at send time.
/// </summary>
public static class NotificationMessages
{
    private static LocalizationManager L => LocalizationManager.Instance;

    /// <summary>Title shown on a medication reminder, e.g. "Bella's dose".</summary>
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

    /// <summary>Title for the once-a-day care reminder, e.g. "Charly's care today".</summary>
    public static string DailyCareTitle(string petName)
        => L.Format("Notif_DailyCareTitle", SafePet(petName));

    /// <summary>
    /// Body for the daily care reminder. Deliberately carries NO count of what's
    /// pending: a number set when the notification is armed (often hours before it
    /// fires) can be stale by the time it shows, and a stale number reads as wrong.
    /// A plain, always-true line can't be. States a fact, never a nudge (§8).
    /// </summary>
    public static string DailyCareBody(string petName)
        => L.Format("Notif_DailyCareBody", SafePet(petName));

    // ── Reserved for future reminder types ───────────────────────────────

    public static string MoodCheckInTitle(string petName) => L.Format("Notif_MoodCheckInTitle", SafePet(petName));
    public static string MoodCheckInBody(string petName) => L.Format("Notif_MoodCheckInBody", SafePet(petName));

    public static string WeightCheckInTitle(string petName) => L.Format("Notif_WeightCheckInTitle", SafePet(petName));
    public static string WeightCheckInBody(string petName) => L.Format("Notif_WeightCheckInBody", SafePet(petName));

    // ── The one reminder before a vet visit ──────────────────────────────
    //
    // Verbatim the approved pattern in AI/app-voice.md §8: it names the day, the pet
    // and the time, says the summary is ready, and stops. No urgency word, nothing
    // about the animal's condition, and no count of anything.

    /// <summary>"Vet visit tomorrow". No pet name: the body carries it, and a title
    /// that reads as a fact about the day is calmer on a lock screen than one that
    /// opens with a name.</summary>
    public static string AppointmentTitle() => L.GetString("Notif_AppointmentTitle");

    /// <summary>"Charly, 9:30. Your summary is ready." — or without the time when the
    /// owner only knew the day. The app never fabricates the missing half.</summary>
    public static string AppointmentBody(string petName, TimeSpan? time)
        => time is TimeSpan t
            ? L.Format("Notif_AppointmentBody", SafePet(petName), t.ToString(@"hh\:mm"))
            : L.Format("Notif_AppointmentBodyNoTime", SafePet(petName));

    private static string SafePet(string petName)
        => string.IsNullOrWhiteSpace(petName) ? L.GetString("Notif_SafePet") : petName.Trim();

    private static string SafeMed(string medicationName)
        => string.IsNullOrWhiteSpace(medicationName) ? L.GetString("Notif_DefaultMedication") : medicationName.Trim();
}
