namespace Animal_Diary_App.Data.Services.Journal;

using System.Diagnostics;
using System.Globalization;
using Animal_Diary_App.Data.Models;
using SQLite;

/// <summary>
/// Answers "which unit does this owner see for this record?", by counting their own
/// entries. The gathering half of <see cref="DisplayUnitResolver"/>: the same split as
/// <c>PendingItemsService</c> / <see cref="PendingEngine"/>, so every rule worth
/// testing lives in a pure function and this file only knows how to read rows.
///
/// <para><b>Derived per call, never persisted.</b> There is no "display unit" column
/// anywhere and there must never be one: the answer is a function of the entries, so it
/// cannot drift out of step with them, and an owner who switches to pounds gets pounds
/// everywhere the moment most of their weigh-ins are in pounds. Same shape as
/// <c>Pet.AgeYears</c>.</para>
///
/// <para><b>The remembered preference is a different thing and lives here too, kept
/// apart on purpose.</b> Display is derived from data; <see cref="GetRememberedAsync"/>
/// is what PREFILLS a picker, and it is device-scoped rather than per pet, because
/// someone who logs Mira in lb will log Kira in lb too. It is a preference, not medical
/// data, so it sits in <c>AppSettings</c> beside the Today card pair and is wiped by a
/// reset like everything else.</para>
/// </summary>
public class DisplayUnitService
{
    private readonly SQLiteAsyncConnection _db;
    private readonly SettingsService _settings;

    public DisplayUnitService(AppDatabase database, SettingsService settings)
    {
        _db = database.Connection;
        _settings = settings;
    }

    /// <summary>The AppSettings key holding the last unit picked for a family. The
    /// family's NAME, not its number: an enum reordered later must not silently hand
    /// someone else's preference back.</summary>
    public static string PreferenceKey(UnitFamily family) => $"Units:{family}";

    // ── Which store holds a family's readings ────────────────────────────────
    //
    //  One row per family, in the same spirit as TodayCardCatalog: the table, the
    //  unit column, and the predicate that says a row actually carries a reading.
    //  Weight is the one with a predicate: PetEntry is the mood+weight row, so a
    //  mood-only day is a row with no weigh-in on it and must not vote.

    private readonly record struct Store(string Table, string UnitColumn, string Predicate);

    private static Store StoreFor(UnitFamily family) => family switch
    {
        UnitFamily.Weight => new("PetEntry", "WeightUnit", " and Weight > 0"),
        UnitFamily.Glucose => new("GlucoseEntry", "Unit", ""),
        UnitFamily.Volume => new("WaterAmountEntry", "Unit", ""),
        UnitFamily.FoodMass => new("AppetiteAmountEntry", "Unit", ""),
        UnitFamily.Duration => new("SeizureEntry", "Unit", ""),
        _ => throw new ArgumentOutOfRangeException(nameof(family)),
    };

    /// <summary>One unit's share of a pet's history, as SQL hands it back.</summary>
    private sealed class TallyRow
    {
        public string? UnitId { get; set; }
        public int Tally { get; set; }
        public DateTime LastAt { get; set; }
    }

    /// <summary>
    /// The unit every surface shows this pet's <paramref name="tracker"/> readings in.
    ///
    /// <para><b>Across the whole history, not the range on screen.</b> Resolving per
    /// range would flip the unit as the owner dragged a chart's range selector, and a
    /// chart whose axis changes meaning while you look at it reads as a bug. Stability
    /// beats local optimality.</para>
    ///
    /// <para>Returns null for a record with no convertible number: mood (no number at
    /// all) and an owner-defined tracker (a number in free-text units nothing can
    /// convert). Callers show those exactly as they were written.</para>
    /// </summary>
    public async Task<UnitDef?> ResolveAsync(int petId, TrackerId tracker)
    {
        if (UnitCatalog.FamilyFor(tracker) is not UnitFamily family)
            return null;

        return await ResolveAsync(petId, family);
    }

    /// <summary>Same, named by family: for the surfaces that already know which set of
    /// units they are in (the vet report's water section, a sheet's picker).</summary>
    public async Task<UnitDef> ResolveAsync(int petId, UnitFamily family)
    {
        var tallies = await TallyAsync(petId, family);
        var remembered = await GetRememberedAsync(family);
        return DisplayUnitResolver.Resolve(family, tallies, remembered, CurrentRegion());
    }

    /// <summary>
    /// The units this pet's entries <b>inside a range</b> were written in, other than the
    /// one they are being shown in. Empty when nothing was converted.
    ///
    /// <para>The vet report asks, so it can say so in its footer: a value silently
    /// converted inside a medical document is exactly the kind of thing that should be
    /// stated. It is a fact about the record, so it sits comfortably inside the report's
    /// owner-facts-only rule.</para>
    ///
    /// <para><b>Range-scoped, unlike the resolution itself.</b> The unit a report is
    /// written in follows the whole history (so it is stable), but the disclosure is
    /// about the values ON THIS DOCUMENT: telling a vet that something was converted when
    /// nothing in front of them was would be noise, and staying silent when something was
    /// is the failure this exists to prevent.</para>
    /// </summary>
    public async Task<IReadOnlyList<UnitDef>> OtherUnitsInRangeAsync(
        int petId, UnitFamily family, UnitDef shownIn, DateTime from, DateTime to)
    {
        var others = new List<UnitDef>();
        foreach (var tally in await TallyAsync(petId, family, from, to))
            if (tally.Count > 0 && tally.UnitId != shownIn.Id && UnitCatalog.IsKnown(family, tally.UnitId))
                others.Add(UnitCatalog.Get(family, tally.UnitId));
        return others;
    }

    /// <summary>
    /// Every unit this pet's entries for a family were written in, with counts and the
    /// latest DAY each was used.
    ///
    /// <para>Grouped in SQL rather than read row by row: this runs on every surface that
    /// shows a reading, and a pet with three years of thrice-daily glucose has thousands
    /// of rows. <c>max(Date)</c> rather than the exact moment because the date column's
    /// storage format (ticks or ISO text) is sqlite-net's business, and both are
    /// monotonic under <c>max</c> while <c>Date + Time</c> would only be right for one of
    /// them. A tie between two units whose latest entries fall on the same DAY is settled
    /// deterministically by the resolver.</para>
    /// </summary>
    private async Task<IReadOnlyList<UnitTally>> TallyAsync(
        int petId, UnitFamily family, DateTime? from = null, DateTime? to = null)
    {
        var store = StoreFor(family);
        var canonical = UnitCatalog.Canonical(family).Id;

        try
        {
            // A NULL unit is a row written before units existed, and it means the
            // canonical unit: coalesced here so the group-by folds legacy rows in with
            // any the owner later logs in the canonical unit, rather than into a bucket
            // of their own that could out-vote it.
            // The range is optional and the two bind positions are appended in step with
            // the predicate, so the same statement serves both the whole-history
            // resolution and the report's range-scoped disclosure.
            var window = from is null || to is null ? string.Empty : " and Date >= ? and Date <= ?";
            var sql =
                $"select coalesce(\"{store.UnitColumn}\", ?) as UnitId, count(*) as Tally, "
                + $"max(Date) as LastAt from \"{store.Table}\" "
                + $"where PetId = ? and IsDeleted = 0{store.Predicate}{window} group by UnitId";

            var rows = window.Length == 0
                ? await _db.QueryAsync<TallyRow>(sql, canonical, petId)
                : await _db.QueryAsync<TallyRow>(sql, canonical, petId, from!.Value.Date, to!.Value.Date);

            var tallies = new List<UnitTally>(rows.Count);
            foreach (var row in rows)
                tallies.Add(new UnitTally(row.UnitId ?? canonical, row.Tally, row.LastAt));
            return tallies;
        }
        catch (Exception ex)
        {
            // A failed count must degrade to "no entries" (the remembered pick, then the
            // locale guess), never crash a page that was only trying to render a number.
            Debug.WriteLine($"Error tallying {family} units for pet {petId}: {ex.Message}");
            return Array.Empty<UnitTally>();
        }
    }

    /// <summary>The last unit the owner picked for this family on any sheet, or null.
    /// Used to prefill a picker, and as the fallback for a record with no entries yet.</summary>
    public async Task<string?> GetRememberedAsync(UnitFamily family)
    {
        var stored = await _settings.GetValueAsync(PreferenceKey(family));
        return UnitCatalog.IsKnown(family, stored) ? stored : null;
    }

    /// <summary>Remember what the owner just picked, so the next sheet opens on it.
    /// Called on save, never on merely opening a picker: what they chose and kept is the
    /// signal, and a stray tap while browsing is not.</summary>
    public Task RememberAsync(UnitDef unit) =>
        _settings.SetValueAsync(PreferenceKey(unit.Family), unit.Id);

    /// <summary>The device's region for the last-resort guess. Invariant culture has no
    /// region and throws, so an unknowable region reads as metric, which is the world's
    /// answer for all but three countries.</summary>
    private static string? CurrentRegion()
    {
        try
        {
            return RegionInfo.CurrentRegion.TwoLetterISORegionName;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"No current region: {ex.Message}");
            return null;
        }
    }
}
