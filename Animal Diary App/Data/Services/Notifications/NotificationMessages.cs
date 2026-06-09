namespace Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// Central home for every piece of notification copy.
///
/// Tone guide: warm, supportive and personal — we speak directly to the carer
/// and always name the pet. Notifications should feel like a gentle nudge from
/// a friend, never a robotic system alert. Keep one emoji at most so the
/// message stays calm.
/// </summary>
public static class NotificationMessages
{
    /// <summary>Title shown on a medication reminder, e.g. "Time for Bella's medication ❤️".</summary>
    public static string MedicationTitle(string petName)
        => $"Time for {SafePet(petName)}'s medication ❤️";

    /// <summary>
    /// Body for a medication reminder. The phrasing gently rotates by
    /// <paramref name="slot"/> so a pet on several daily doses doesn't see the
    /// exact same sentence every time.
    /// </summary>
    public static string MedicationBody(string petName, string medicationName, int slot)
    {
        var pet = SafePet(petName);
        var med = string.IsNullOrWhiteSpace(medicationName) ? "medication" : medicationName.Trim();

        var phrasings = new[]
        {
            $"It's time for {pet}'s {med} ❤️",
            $"A quick reminder: {pet}'s {med} is due.",
            $"Don't forget {pet}'s {med} — they're counting on you 🐾",
            $"A little nudge: {pet}'s {med} is ready when you are.",
            $"{pet}'s {med} is due now. You've got this 💜"
        };

        return phrasings[Math.Abs(slot) % phrasings.Length];
    }

    // ── Reserved for future reminder types ───────────────────────────────

    public static string MoodCheckInTitle(string petName) => $"How is {SafePet(petName)} today? 🐾";
    public static string MoodCheckInBody(string petName)
        => $"Don't forget to log how {SafePet(petName)} is feeling today.";

    public static string WeightCheckInTitle(string petName) => $"{SafePet(petName)}'s weigh-in";
    public static string WeightCheckInBody(string petName)
        => $"A quick weigh-in helps you keep {SafePet(petName)} healthy and happy.";

    public static string AppointmentTitle(string petName) => $"Upcoming for {SafePet(petName)} ❤️";
    public static string AppointmentBody(string petName, string what)
        => $"A gentle heads-up: {SafePet(petName)}'s {what} is coming up.";

    private static string SafePet(string petName)
        => string.IsNullOrWhiteSpace(petName) ? "your pet" : petName.Trim();
}
