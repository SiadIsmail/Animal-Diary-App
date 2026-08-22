namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  "Since your last visit": the screen this whole feature exists for.
//
//  It is ASSEMBLED, never stored. Nothing about the summary is written down: it is
//  the treatment ledger, the facts service and the question list, read over one
//  window and handed over as a snapshot. Storing it would mean a summary that
//  disagreed with the diary the moment anything was edited.
//
//  The window is (the most recent PAST visit) .. today. If there is no previous
//  visit the anchor is the pet's first entry instead, and the wording says so,
//  the app never invents a visit that did not happen.
//
//  THE LINE THIS MUST NOT CROSS. Everything here is a fact about the record:
//  counts, dates, the owner's own numbers, and the words they typed themselves.
//  There is no average, no trend, no direction, no ranking, and (explicitly) no
//  before/after juxtaposition of two counts around a treatment change. Two counts
//  either side of a dose change ARGUE; whether Felova is allowed to argue is a
//  decision that has not been made. See Data/Models/RecordFacts.cs.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One thing the owner started writing down inside the window. A fact about
/// the diary: <b>not</b> a claim about why. The app cannot tell a change in the animal
/// from a change in the logging, so it says only when the first entry appeared.</summary>
/// <param name="Name">The record's name, localized for a shipped tracker and verbatim
/// user text for an owner-defined one.</param>
/// <param name="FirstOn">The date of its first-ever entry.</param>
public sealed record NewRecord(string Name, DateTime FirstOn);

/// <summary>One record's facts, with the name to head them.</summary>
/// <param name="Name">Localized for a shipped record, verbatim for a custom tracker.</param>
/// <param name="Unit">The unit the facts' numbers are to be shown in, resolved from the
/// owner's own entries; null for a record with no convertible unit. The facts stay
/// canonical (AI/domain.md, Units).</param>
public sealed record SummaryRecord(string Name, RecordFacts Facts, UnitDef? Unit = null);

/// <summary>
/// Everything the "since your last visit" screen shows, assembled from the stores.
/// </summary>
/// <param name="Anchor">The visit the window measures from, or null when there is
/// none.</param>
/// <param name="From">Start of the window: the anchor visit's date, or the pet's
/// first entry when there is no previous visit.</param>
/// <param name="Days">How long the window is, inclusive.</param>
/// <param name="Changes">The treatment ledger over the window, chronological.</param>
/// <param name="NewRecords">Trackers whose first-ever entry falls inside the window.
/// Empty when there is no previous visit: with the window covering all of history
/// everything would qualify, which says nothing.</param>
/// <param name="Records">Every record the pet has data for, in one fixed order, each
/// stated the same way: including the ones with nothing interesting in them.</param>
/// <param name="Questions">The owner's open questions, oldest first.</param>
public sealed record AppointmentSummary(
    VetVisit? Anchor,
    DateTime From,
    DateTime To,
    int Days,
    IReadOnlyList<MedicationChange> Changes,
    IReadOnlyList<NewRecord> NewRecords,
    IReadOnlyList<SummaryRecord> Records,
    IReadOnlyList<VetQuestion> Questions)
{
    /// <summary>There is a previous visit to measure from. False means the window runs
    /// from the pet's first entry and the copy must say so.</summary>
    public bool HasAnchor => Anchor is not null;

    public static AppointmentSummary Empty(DateTime today) => new(
        null, today, today, 1,
        Array.Empty<MedicationChange>(), Array.Empty<NewRecord>(),
        Array.Empty<SummaryRecord>(), Array.Empty<VetQuestion>());
}

public class AppointmentSummaryService
{
    private readonly VetVisitService _visits;
    private readonly VetQuestionService _questions;
    private readonly RecordFactsService _facts;
    private readonly CustomTrackerService _custom;
    private readonly MedicationService _medications;

    public AppointmentSummaryService(
        VetVisitService visits,
        VetQuestionService questions,
        RecordFactsService facts,
        CustomTrackerService custom,
        MedicationService medications)
    {
        _visits = visits;
        _questions = questions;
        _facts = facts;
        _custom = custom;
        _medications = medications;
    }

    /// <summary>Everything, with no floor of our own. A sentinel date would silently
    /// hide an entry imported from before it, and "up to X" is a scan either way: the
    /// lower bound buys nothing to be clever about.</summary>
    private static DateTime Everything => DateTime.MinValue;

    /// <summary>
    /// Assemble the summary for one pet.
    ///
    /// <para>Reads are issued one at a time, never in a <c>Task.WhenAll</c>: sqlite-net's
    /// async API queues each call to the thread pool where it takes a lock on the one
    /// shared connection, so concurrency here would occupy N threads to run one query
    /// (AI/coding-standards.md). This page is opened a handful of times a year.</para>
    /// </summary>
    public async Task<AppointmentSummary> BuildAsync(Pet? pet)
    {
        var today = DateTime.Today;
        if (pet is null || pet.Id == 0)
            return AppointmentSummary.Empty(today);

        var anchor = await _visits.GetMostRecentPastAsync(pet.Id);

        // The archived-inclusive list: a tracker retired last month still recorded
        // things inside the window, and dropping it would silently shorten the history
        // the vet is being shown.
        var definitions = await _custom.GetAllForPetAsync(pet.Id);
        var kinds = BuildKindList(definitions);

        // Without a previous visit the window has to start somewhere real: the pet's
        // first entry. One pass over all of history per kind finds it, and the same
        // pass IS the window facts, since the window is then all of history.
        var from = anchor?.Date.Date ?? await FirstEntryDateAsync(pet, kinds, today);

        var records = new List<SummaryRecord>(kinds.Count);
        var newRecords = new List<NewRecord>();

        foreach (var (key, name) in kinds)
        {
            // The snapshot rather than the facts alone: it carries the unit the owner's
            // own entries resolved to, and this document is read by a clinician, so
            // every number in it has to name its unit (AI/domain.md, Units).
            var snapshot = await _facts.GetSnapshotAsync(pet, key, from, today);
            var facts = snapshot.Facts;
            if (!facts.HasAny)
                continue;                       // nothing to state about it in this window

            records.Add(new SummaryRecord(name, facts, snapshot.DisplayUnit));

            // "New since last time" needs a last time. It also needs a SECOND read,
            // "did this record exist before the window?", which is why it is asked
            // only for records that actually have entries in the window.
            if (anchor is null || key.Is(TodayCardId.Medication))
                continue;                       // doses are not a tracker

            var before = await _facts.GetAsync(pet, key, Everything, from.AddDays(-1));
            if (!before.HasAny && facts.FirstOn is DateTime first)
                newRecords.Add(new NewRecord(name, first));
        }

        // The ledger is stamped in UTC; the window is local dates. Widen by a day at
        // each end rather than converting: a change made at 23:00 local on the visit
        // day belongs in the window, and an hour's slop cannot put a row in the wrong
        // consultation.
        var changes = await _medications.GetChangesForRangeAsync(
            pet.Id, from.AddDays(-1).ToUniversalTime(), today.AddDays(2).ToUniversalTime());

        var questions = await _questions.GetOpenAsync(pet.Id);

        return new AppointmentSummary(
            anchor, from, today,
            Math.Max(1, (today - from).Days + 1),
            changes, newRecords, records, questions);
    }

    /// <summary>
    /// Every record this pet could have written something down about, in ONE fixed
    /// order: the shipped cards in catalog order, then the owner's own trackers.
    ///
    /// <para>Fixed and complete on purpose. Ordering by how much is in each, or dropping
    /// the quiet ones, would be the app deciding which record matters, and a summary
    /// that lists seizures first only when there were many is a summary that has started
    /// making a point.</para>
    /// </summary>
    private static List<(TodayCardKey Key, string Name)> BuildKindList(IReadOnlyList<CustomTracker> definitions)
    {
        var kinds = new List<(TodayCardKey, string)>(TodayCardCatalog.Cards.Count + definitions.Count);

        foreach (var meta in TodayCardCatalog.Cards)
            kinds.Add((meta.Id, TodayCardCatalog.RecordName(meta.Id)));

        // Their own name, verbatim, never translated (AI/coding-standards.md).
        foreach (var c in definitions)
            kinds.Add((TodayCardKey.Custom(c.Id), c.Name));

        return kinds;
    }

    /// <summary>The earliest thing this pet has written down, across every record, or
    /// today when there is nothing at all. Only reached when the pet has never had a
    /// visit: with one, the anchor's date is the window and none of this runs.</summary>
    private async Task<DateTime> FirstEntryDateAsync(
        Pet pet, IReadOnlyList<(TodayCardKey Key, string Name)> kinds, DateTime today)
    {
        DateTime? earliest = null;
        foreach (var (key, _) in kinds)
        {
            var facts = await _facts.GetAsync(pet, key, Everything, today);
            if (facts.FirstOn is DateTime first && (earliest is null || first < earliest))
                earliest = first;
        }
        return earliest ?? today;
    }
}
