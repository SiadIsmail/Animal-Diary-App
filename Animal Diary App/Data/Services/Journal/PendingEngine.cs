namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// The single, pure function behind the Journal's "Still to do" row:
/// <see cref="Compute"/> answers "what is left to log for this pet, today?".
///
/// It is deliberately pure — no database, no clock, no async. The caller gathers a
/// snapshot (today's scheduled doses with their given-state, and each tracker's
/// recent entry dates) and this decides what's pending. That makes every rule here
/// trivially unit-testable, and keeps the medical logic in one auditable place.
///
/// Rules (all relative to <paramref name="date"/>, which is always TODAY in the app):
///   • Medication doses — every scheduled dose today not yet given. Insulin is a
///     medication like any other; nothing here is special-cased.
///   • PerDay trackers (glucose) — pending while today's count &lt; PerDayCount; the
///     item carries "{done} of {target}" for the chip.
///   • Daily — pending if there's no entry today.
///   • Weekly — pending if no entry in the rolling last 7 days.
///   • TwiceWeekly — pending if no entry in the rolling last 3 days.
///   • AsNeeded and Event — never pending.
/// Order: med doses first (soonest due time first), then PerDay, then the rest in
/// care-plan order.
/// </summary>
public static class PendingEngine
{
    /// <param name="carePlan">The pet's trackers.</param>
    /// <param name="dosesToday">Every dose scheduled for <paramref name="date"/>, each
    /// flagged with whether it has been given.</param>
    /// <param name="entryDatesByTracker">Per tracker, the date-only stamps of its recent
    /// entries — one element per entry, so same-day repeats (multiple glucose reads)
    /// are counted. Must cover at least the last 7 days for weekly checks to be
    /// correct. Missing keys are treated as "no entries".</param>
    /// <param name="date">The day being evaluated — TODAY.</param>
    public static IReadOnlyList<PendingItem> Compute(
        IReadOnlyList<Tracker> carePlan,
        IReadOnlyList<ScheduledDose> dosesToday,
        IReadOnlyDictionary<TrackerId, IReadOnlyList<DateTime>> entryDatesByTracker,
        DateTime date)
    {
        var today = date.Date;
        var result = new List<PendingItem>();

        // 1) Medication doses not yet given, soonest due time first. A stable
        //    secondary sort by name keeps two doses at the same time deterministic.
        foreach (var dose in dosesToday
                     .Where(d => !d.Given)
                     .OrderBy(d => d.Time)
                     .ThenBy(d => d.MedicationName, StringComparer.CurrentCultureIgnoreCase))
        {
            result.Add(PendingItem.ForDose(dose.PetId, dose.MedicationId, dose.MedicationName, dose.Time));
        }

        // 2) PerDay trackers (glucose), in care-plan order.
        foreach (var t in carePlan.Where(t => t.Kind == TrackerKind.PerDay))
        {
            var done = CountOn(entryDatesByTracker, t.TrackerId, today);
            if (done < t.PerDayCount)
                result.Add(PendingItem.ForTracker(t.TrackerId, done, t.PerDayCount));
        }

        // 3) Everything else, in care-plan order. AsNeeded / Event never qualify.
        foreach (var t in carePlan)
        {
            bool pending = t.Kind switch
            {
                TrackerKind.Daily => !AnyInWindow(entryDatesByTracker, t.TrackerId, today, days: 1),
                TrackerKind.Weekly => !AnyInWindow(entryDatesByTracker, t.TrackerId, today, days: 7),
                TrackerKind.TwiceWeekly => !AnyInWindow(entryDatesByTracker, t.TrackerId, today, days: 3),
                _ => false // PerDay handled above; AsNeeded and Event are never pending.
            };

            if (pending)
                result.Add(PendingItem.ForTracker(t.TrackerId));
        }

        return result;
    }

    /// <summary>
    /// The day's care measured in units — the numerator/denominator behind the Today
    /// page's care ring. Counts the SAME rules as <see cref="Compute"/> so the ring
    /// and the "Still to do" chips can never disagree: each dose is one unit, a
    /// PerDay tracker is PerDayCount units, Daily/Weekly/TwiceWeekly trackers are one
    /// unit each (done once their rolling window is satisfied), and AsNeeded/Event
    /// contribute nothing. Done == Total exactly when <see cref="Compute"/> returns
    /// no pending items.
    /// </summary>
    public static DayProgress ComputeProgress(
        IReadOnlyList<Tracker> carePlan,
        IReadOnlyList<ScheduledDose> dosesToday,
        IReadOnlyDictionary<TrackerId, IReadOnlyList<DateTime>> entryDatesByTracker,
        DateTime date)
    {
        var today = date.Date;
        int done = dosesToday.Count(d => d.Given);
        int total = dosesToday.Count;

        foreach (var t in carePlan)
        {
            switch (t.Kind)
            {
                case TrackerKind.PerDay:
                    total += t.PerDayCount;
                    done += Math.Min(CountOn(entryDatesByTracker, t.TrackerId, today), t.PerDayCount);
                    break;
                case TrackerKind.Daily or TrackerKind.Weekly or TrackerKind.TwiceWeekly:
                    int window = t.Kind switch
                    {
                        TrackerKind.Daily => 1,
                        TrackerKind.TwiceWeekly => 3,
                        _ => 7
                    };
                    total += 1;
                    done += AnyInWindow(entryDatesByTracker, t.TrackerId, today, window) ? 1 : 0;
                    break;
                    // AsNeeded / Event: never scheduled, never counted.
            }
        }

        return new DayProgress(done, total);
    }

    /// <summary>Number of entries for a tracker on a specific day.</summary>
    private static int CountOn(
        IReadOnlyDictionary<TrackerId, IReadOnlyList<DateTime>> byTracker, TrackerId id, DateTime day)
    {
        return byTracker.TryGetValue(id, out var dates)
            ? dates.Count(d => d.Date == day)
            : 0;
    }

    /// <summary>True if the tracker has any entry within a rolling window of
    /// <paramref name="days"/> days ending on (and including) <paramref name="today"/>.
    /// days:1 → today only; days:7 → today and the previous six.</summary>
    private static bool AnyInWindow(
        IReadOnlyDictionary<TrackerId, IReadOnlyList<DateTime>> byTracker, TrackerId id, DateTime today, int days)
    {
        if (!byTracker.TryGetValue(id, out var dates))
            return false;

        var from = today.AddDays(-(days - 1));
        return dates.Any(d => d.Date >= from && d.Date <= today);
    }
}
