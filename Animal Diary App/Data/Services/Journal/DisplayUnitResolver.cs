namespace Animal_Diary_App.Data.Services.Journal;

using Animal_Diary_App.Data.Models;

/// <summary>
/// One record's tally in one unit: how many of the pet's entries were written in it,
/// and when the last of those was. Both halves are needed: the count decides the
/// majority, the stamp breaks a tie.
/// </summary>
/// <param name="UnitId">The stored unit id. A legacy row with no unit is counted here
/// under its family's CANONICAL id, because that is exactly what it means.</param>
/// <param name="Count">Entries written in this unit, across the pet's whole history.</param>
/// <param name="LastAt">When the most recent of them was recorded.</param>
public readonly record struct UnitTally(string UnitId, int Count, DateTime LastAt);

/// <summary>
/// The single, pure function behind "which unit does this owner see?": the same
/// pure-engine / gathering-service split as <see cref="PendingEngine"/> and
/// <c>PendingItemsService</c>. <c>DisplayUnitService</c> gathers the tallies from the
/// entry stores; this decides.
///
/// <para><b>The display unit is DERIVED, never stored.</b> There is no settings screen,
/// no migration prompt and no "choose your units" step: whichever unit the owner has
/// actually used most for that record is the unit every dashboard, chart, summary and
/// report shows, and minority entries are converted into it. The same shape as
/// <c>Pet.AgeYears</c> and <c>TodayCardCatalog.DefaultsFor</c>: this codebase prefers a
/// derived answer to a stored preference.</para>
///
/// <para>The order of the rules, and why each one is where it is:</para>
/// <list type="number">
/// <item><b>Majority</b> of the pet's entries for that record.</item>
/// <item><b>Tie → the most recent.</b> Deterministic, and it follows where the owner
/// is heading. Among the units tied at the top, the one whose latest entry is latest:
/// a unit that is not tied for the lead cannot win a tie it is not in. Stamps that are
/// themselves equal fall to the canonical unit and then to the lesser id, so the answer
/// never depends on the order the rows happened to come back in.</item>
/// <item><b>No entries at all → the remembered device preference</b> for that family
/// (what the owner last picked on a sheet, for any pet).</item>
/// <item><b>No preference → a locale guess</b> (<see cref="UnitCatalog.LocaleDefault"/>).</item>
/// </list>
///
/// <para><b>Whole history, not the visible range.</b> Resolving over a range would flip
/// the unit as the owner dragged a chart's range selector, which is maddening and reads
/// as a bug. Stability beats local optimality.</para>
///
/// <para><b>Per (pet, record).</b> A US owner logs weight in lb and glucose in mg/dL;
/// deriving each independently gets both right with no extra concept and no screen.</para>
/// </summary>
public static class DisplayUnitResolver
{
    /// <param name="family">Which unit set the record is measured in.</param>
    /// <param name="tallies">Every unit the pet's entries for this record were written
    /// in, with counts and last-written stamps. Empty when nothing was ever logged.
    /// Legacy rows carrying no unit belong under the canonical id (see
    /// <see cref="UnitTally"/>).</param>
    /// <param name="remembered">The device's last picked unit id for this family, or
    /// null. Used ONLY when there are no entries: what the owner has written down
    /// outranks what they last tapped.</param>
    /// <param name="regionCode">Two-letter ISO region for the last-resort guess.</param>
    public static UnitDef Resolve(
        UnitFamily family,
        IReadOnlyList<UnitTally> tallies,
        string? remembered,
        string? regionCode)
    {
        var best = default(UnitTally);
        var found = false;

        foreach (var tally in tallies)
        {
            if (tally.Count <= 0)
                continue;

            // Unknown ids are skipped rather than counted as themselves: the stored
            // NUMBER is canonical regardless, so a unit this build does not know about
            // must not be able to win the vote and then be rendered as canonical anyway.
            if (!UnitCatalog.IsKnown(family, tally.UnitId))
                continue;

            if (!found || Beats(family, tally, best))
            {
                best = tally;
                found = true;
            }
        }

        if (found)
            return UnitCatalog.Get(family, best.UnitId);

        // Nothing written down yet. The remembered pick is a device preference, not a
        // fact about this pet: someone who logs Mira in lb will log Kira in lb too.
        if (UnitCatalog.IsKnown(family, remembered))
            return UnitCatalog.Get(family, remembered);

        return UnitCatalog.LocaleDefault(family, regionCode);
    }

    /// <summary>
    /// Whether <paramref name="challenger"/> should displace <paramref name="holder"/>.
    ///
    /// <para>Majority first. The stamp comparison therefore only ever runs BETWEEN UNITS
    /// TIED AT THE TOP: anything with fewer entries has already lost. The last two rungs
    /// exist so the answer is a function of the data alone rather than of the order the
    /// rows came back in: stamps are gathered per DAY (see <c>DisplayUnitService</c>), so
    /// two units whose latest entries fall on the same day would otherwise be separated
    /// by nothing at all. Canonical wins that, then the lesser id.</para>
    /// </summary>
    private static bool Beats(UnitFamily family, UnitTally challenger, UnitTally holder)
    {
        if (challenger.Count != holder.Count)
            return challenger.Count > holder.Count;

        if (challenger.LastAt != holder.LastAt)
            return challenger.LastAt > holder.LastAt;

        var canonical = UnitCatalog.Canonical(family).Id;
        if (challenger.UnitId == canonical)
            return holder.UnitId != canonical;
        if (holder.UnitId == canonical)
            return false;

        return string.CompareOrdinal(challenger.UnitId, holder.UnitId) < 0;
    }

    /// <summary>
    /// Fold a pet's entries into tallies. The stores hand over one (unit, recorded-at)
    /// pair per row; a null or empty unit is a legacy row and counts as canonical,
    /// which is precisely what it is.
    /// </summary>
    public static IReadOnlyList<UnitTally> Tally(
        UnitFamily family,
        IEnumerable<(string? UnitId, DateTime At)> entries)
    {
        var canonical = UnitCatalog.Canonical(family).Id;
        var counts = new Dictionary<string, (int Count, DateTime LastAt)>(StringComparer.Ordinal);

        foreach (var (unitId, at) in entries)
        {
            var id = string.IsNullOrWhiteSpace(unitId) ? canonical : unitId!;
            if (counts.TryGetValue(id, out var existing))
                counts[id] = (existing.Count + 1, at > existing.LastAt ? at : existing.LastAt);
            else
                counts[id] = (1, at);
        }

        var result = new List<UnitTally>(counts.Count);
        foreach (var pair in counts)
            result.Add(new UnitTally(pair.Key, pair.Value.Count, pair.Value.LastAt));
        return result;
    }
}
