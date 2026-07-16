namespace Animal_Diary_App.Data.Models;

/// <summary>
/// Builds the DEFAULT care plan for a pet: the always-on trackers every pet starts
/// with, plus the extra trackers a condition introduces. This is the seed only —
/// once persisted, a pet's <see cref="Tracker"/> rows are the source of truth and
/// the (later) pet page tunes them.
///
/// Medications are intentionally absent: a condition may suggest medications, but
/// those are created in the medication flow and read by the pending engine straight
/// from the med schedule. Insulin is never modelled here.
///
/// This mirrors the condition→trackers mapping the pet-page prototype demonstrates,
/// expressed in the app's cadence vocabulary (<see cref="TrackerKind"/>).
/// </summary>
public static class CarePlanCatalog
{
    /// <summary>Every pet starts here: a daily mood check and a weekly weigh-in.</summary>
    public static List<Tracker> DefaultTrackers() => new()
    {
        Simple(TrackerId.Mood, TrackerKind.Daily),
        Simple(TrackerId.Weight, TrackerKind.Weekly),
    };

    /// <summary>Extra trackers a condition adds, in the app's own vocabulary. Weight
    /// and Mood already come from the defaults, so conditions only add what's new.</summary>
    public static IReadOnlyList<Tracker> ForCondition(string? conditionId) => (conditionId ?? string.Empty) switch
    {
        "diabetes" => new[]
        {
            Glucose(perDayCount: 3, fromCondition: "diabetes"),   // mmol/L, no target range yet
        },
        "ckd" => new[]
        {
            Simple(TrackerId.Appetite, TrackerKind.Daily, "ckd"),
            Simple(TrackerId.Water, TrackerKind.Daily, "ckd"),
        },
        "epilepsy" => new[]
        {
            Simple(TrackerId.Seizure, TrackerKind.Event, "epilepsy"),
        },
        // "" (none) and any unknown id contribute no extra trackers.
        _ => System.Array.Empty<Tracker>(),
    };

    /// <summary>The full default plan for a pet with the given condition: defaults +
    /// condition extras, de-duplicated by <see cref="Tracker.TrackerId"/> (defaults win).</summary>
    public static List<Tracker> BuildDefaultPlan(string? conditionId) =>
        BuildDefaultPlan(new[] { conditionId });

    /// <summary>The full default plan for a pet carrying SEVERAL conditions: the
    /// always-on defaults plus every condition's extras, merged and de-duplicated by
    /// <see cref="Tracker.TrackerId"/> (defaults and earlier conditions win). This is
    /// the multi-condition seed the pet page and picker use.</summary>
    public static List<Tracker> BuildDefaultPlan(IEnumerable<string?> conditionIds)
    {
        var plan = new List<Tracker>();
        var seen = new HashSet<TrackerId>();

        void Add(Tracker t)
        {
            if (seen.Add(t.TrackerId))
                plan.Add(t);
        }

        foreach (var t in DefaultTrackers()) Add(t);
        foreach (var id in conditionIds ?? System.Array.Empty<string?>())
            foreach (var t in ForCondition(id)) Add(t);
        return plan;
    }

    // ── Factories ─────────────────────────────────────────────────────────────
    private static Tracker Simple(TrackerId id, TrackerKind kind, string? fromCondition = null) => new()
    {
        TrackerId = id,
        Kind = kind,
        FromCondition = fromCondition
    };

    private static Tracker Glucose(int perDayCount, string? fromCondition) => new()
    {
        TrackerId = TrackerId.Glucose,
        Kind = TrackerKind.PerDay,
        PerDayCount = perDayCount,
        Unit = "mmol/L",
        FromCondition = fromCondition
    };
}
