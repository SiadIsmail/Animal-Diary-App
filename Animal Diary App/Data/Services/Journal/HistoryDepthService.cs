namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// How long this pet's record has been accumulating: days since its first entry, as the
/// coarse bucket <see cref="AnalyticsHistory"/> defines.
///
/// <para>It exists for exactly one caller — the <c>days_of_history</c> property on the
/// paywall and purchase events, which is the only measurement that can test whether the
/// paid boundary's central claim is true. Nothing about access reads it, and nothing may
/// start to: it is telemetry, and a gate that depended on how much someone had written
/// down would be the app charging by the weight of their animal's illness.</para>
///
/// <para><b>Cached for the session, and deliberately.</b> Finding the first entry means
/// one all-of-history read per record kind — the same walk
/// <c>AppointmentSummaryService</c> already does when a pet has never had a visit. That is
/// affordable once; it is not affordable every time a sheet opens. The answer moves by one
/// day at midnight and by nothing else, so a stale value costs a bucket boundary at worst.</para>
///
/// <para>It reuses <c>RecordFactsService</c> over <c>TodayCardCatalog</c>'s kinds plus the
/// pet's own trackers rather than querying tables directly, so a new loggable is counted
/// here the moment it reaches Today's cards — no second list to keep in step.</para>
/// </summary>
public class HistoryDepthService
{
    /// <summary>Everything, with no floor of our own — the same sentinel-free rule
    /// <c>AppointmentSummaryService</c> uses, and for the same reason: a floor would
    /// silently hide entries imported from before it.</summary>
    private static readonly DateTime Everything = DateTime.MinValue;

    private readonly RecordFactsService _facts;
    private readonly CustomTrackerService _custom;

    // petId → (the day the answer was computed, the bucket). Keyed by day so it
    // re-resolves after midnight without a timer.
    private readonly Dictionary<int, (DateTime Day, string Bucket)> _cache = new();

    public HistoryDepthService(RecordFactsService facts, CustomTrackerService custom)
    {
        _facts = facts;
        _custom = custom;
    }

    /// <summary>The bucket for this pet, or <see cref="AnalyticsHistory.None"/> when there
    /// is no pet or nothing has been written down. Never throws: a telemetry property may
    /// never be the thing that breaks a purchase.</summary>
    public async Task<string> BucketAsync(Pet? pet)
    {
        var today = DateTime.Today;
        if (pet is null || pet.Id == 0)
            return AnalyticsHistory.None;

        if (_cache.TryGetValue(pet.Id, out var hit) && hit.Day == today)
            return hit.Bucket;

        try
        {
            var first = await FirstEntryDateAsync(pet, today);
            var bucket = first is DateTime day
                ? AnalyticsHistory.Bucket(AnalyticsHistory.DaysBetween(day, today))
                : AnalyticsHistory.None;

            _cache[pet.Id] = (today, bucket);
            return bucket;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] history depth failed: {ex.Message}");
            return AnalyticsHistory.None;
        }
    }

    /// <summary>The earliest thing this pet has written down across every record, or null
    /// when there is nothing at all.</summary>
    private async Task<DateTime?> FirstEntryDateAsync(Pet pet, DateTime today)
    {
        // Archived-inclusive: a tracker retired last month still recorded things, and the
        // record is exactly as old as its oldest entry whatever happened to the tracker.
        var definitions = await _custom.GetAllForPetAsync(pet.Id);

        DateTime? earliest = null;

        // One read at a time, never Task.WhenAll: sqlite-net queues each async call to the
        // thread pool where it takes a lock on the one shared connection, so concurrency
        // here occupies N threads to run one query (AI/coding-standards.md).
        foreach (var meta in TodayCardCatalog.Cards)
            earliest = Earlier(earliest, await FirstOnAsync(pet, meta.Id, today));

        foreach (var definition in definitions)
            earliest = Earlier(earliest, await FirstOnAsync(pet, TodayCardKey.Custom(definition.Id), today));

        return earliest;
    }

    private async Task<DateTime?> FirstOnAsync(Pet pet, TodayCardKey key, DateTime today)
        => (await _facts.GetAsync(pet, key, Everything, today)).FirstOn;

    private static DateTime? Earlier(DateTime? current, DateTime? candidate) =>
        candidate is DateTime c && (current is null || c < current) ? c : current;
}
