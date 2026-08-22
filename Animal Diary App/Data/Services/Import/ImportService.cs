namespace Animal_Diary_App.Data.Services.Import;

using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Data;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;
using SQLite;

/// <summary>
/// The impure half of the importer: it reads what the device already holds, hands both to
/// the pure <see cref="ImportValidator"/>, and (only if the owner confirms) writes the
/// resulting plan in ONE transaction.
///
/// <para><b>It creates no new kind of row.</b> An imported entry is an ordinary entry from
/// the moment it lands: same tables, same <see cref="SyncStamp"/>, same undo, same vet
/// report, same Constellation. There is deliberately no "imported" flag: a fact about
/// where a weight came from is not a fact about the weight, and every surface in the app
/// would have to learn to ignore it.</para>
///
/// <para><b>Two properties the commit must keep.</b> It is atomic (Android process death
/// mid-write is normal, and half an imported history is worse than none), and every row it
/// writes is stamped, so an import reaches the owner's other devices like anything else.
/// That is why rows are stamped through <see cref="SyncStamp"/> here and inserted raw
/// inside the transaction: <c>RunInTransactionAsync</c> hands out a SYNCHRONOUS connection,
/// so the async entry services cannot be called from inside it. <c>DemoModeService</c> has
/// the same shape and skips the stamping: deliberately, because demo rows must never sync.
/// Import is the opposite case.</para>
/// </summary>
public sealed class ImportService
{
    private readonly AppDatabase _db;
    private readonly PetService _pets;
    private readonly SettingsService _settings;
    private readonly CustomTrackerService _custom;
    private readonly GlucoseEntryService _glucose;
    private readonly SeizureEntryService _seizures;
    private readonly AppetiteEntryService _appetite;
    private readonly WaterEntryService _water;

    public ImportService(
        AppDatabase db,
        PetService pets,
        SettingsService settings,
        CustomTrackerService custom,
        GlucoseEntryService glucose,
        SeizureEntryService seizures,
        AppetiteEntryService appetite,
        WaterEntryService water)
    {
        _db = db;
        _pets = pets;
        _settings = settings;
        _custom = custom;
        _glucose = glucose;
        _seizures = seizures;
        _appetite = appetite;
        _water = water;
    }

    // ── Prepare: parse, gather, validate ────────────────────────────────────────

    /// <summary>Everything that happens before the owner is asked. Never writes.</summary>
    public async Task<ImportPlan> PrepareAsync(string text)
    {
        if (!ImportFileParser.TryParse(text, out var file, out var parseError))
            return ImportPlan.Failed(new[] { parseError });

        var snapshot = await BuildSnapshotAsync(file);
        var plan = ImportValidator.Validate(file, snapshot, DateTime.Today);

        // The one thing the validator cannot know. It is a NOTICE, not a block: a repeat
        // import is legitimate (the owner may have deleted something and want it back),
        // and the content rules below already make it close to a no-op. Worth saying out
        // loud all the same: "you have imported this exact file before" is usually the
        // answer to "why did nothing happen?".
        if (plan.IsValid && await WasSeenBeforeAsync(text))
        {
            plan.FileNotices.Add(new ImportNotice(ImportNoticeKind.AlreadyPresent, ImportLocation.File,
                "This exact file has been imported on this device before. Anything already recorded is skipped."));
        }

        return plan;
    }

    /// <summary>
    /// Read the slice of the database the file could possibly collide with.
    ///
    /// <para>Bounded by the file's own date range so importing a week of notes does not
    /// read a diabetic pet's four years of glucose readings. Pets are matched by name
    /// before anything else loads, so only the animals the file actually names are
    /// touched.</para>
    /// </summary>
    private async Task<ImportSnapshot> BuildSnapshotAsync(ImportFile file)
    {
        // Demo pets are excluded outright: importing into a filming fixture would write
        // rows that can never sync (Pet.IsDemo) onto an animal the creator is about to
        // delete. See AI/domain.md → Demo pets.
        var all = (await _pets.GetPetsAsync()).Where(p => !p.IsDemo).ToList();
        var existing = all.Select(p => new ExistingPet(p.Id, p.Name, p.Type)).ToList();

        var blocks = file.Pets ?? new List<ImportPet>();
        var names = blocks
            .Select(b => (b.Name ?? string.Empty).Trim())
            .Where(n => n.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var candidates = all.Where(p => names.Contains(p.Name.Trim())).ToList();
        if (candidates.Count == 0)
            return new ImportSnapshot { Pets = existing };

        var range = DateRangeOf(blocks);
        if (range is null)
            return new ImportSnapshot { Pets = existing };

        var (from, to) = range.Value;

        var trackers = new List<ExistingTracker>();
        var petDays = new List<ExistingPetDay>();
        var levelDays = new List<ExistingLevelDay>();
        var events = new List<ExistingEvent>();

        foreach (var pet in candidates)
        {
            // Archived-inclusive: matching a retired tracker by name is what stops an
            // import creating a duplicate of something the owner deliberately put away.
            foreach (var t in await _custom.GetAllForPetAsync(pet.Id))
                trackers.Add(new ExistingTracker(t.Id, pet.Id, t.Name, t.Shape, t.IsArchived));

            // ── The three tombstone-INCLUSIVE reads ──────────────────────────────
            //
            // Every other read in this app filters IsDeleted == false, and this is the
            // one place that must not. These three tables are one-row-per-day and the
            // cloud keys them by (pet, day), so a soft-deleted row has to be REVIVED by
            // an import rather than joined by a sibling, and a sibling is invisible
            // locally right up until a second device pulls and the two collapse into one.
            // No repository exposes tombstones (deliberately), so the queries live here,
            // where the reason for them is written down.
            var days = await _db.Connection.Table<PetEntry>()
                .Where(e => e.PetId == pet.Id && e.Date >= from && e.Date <= to)
                .ToListAsync();
            foreach (var d in days)
            {
                petDays.Add(new ExistingPetDay(
                    pet.Id, d.Date.Date, d.Id,
                    HasMood: d.MoodLevel > 0,
                    HasWeight: d.Weight > 0,
                    IsTombstone: d.IsDeleted));
            }

            var appetiteLevels = await _db.Connection.Table<AppetiteEntry>()
                .Where(a => a.PetId == pet.Id && a.Date >= from && a.Date <= to)
                .ToListAsync();
            foreach (var a in appetiteLevels)
                levelDays.Add(new ExistingLevelDay(pet.Id, ImportEntryType.AppetiteLevel, a.Date.Date, a.Id, a.IsDeleted));

            var waterLevels = await _db.Connection.Table<WaterLevelEntry>()
                .Where(w => w.PetId == pet.Id && w.Date >= from && w.Date <= to)
                .ToListAsync();
            foreach (var w in waterLevels)
                levelDays.Add(new ExistingLevelDay(pet.Id, ImportEntryType.WaterLevel, w.Date.Date, w.Id, w.IsDeleted));

            // ── Event fingerprints, LIVE rows only ───────────────────────────────
            //
            // A tombstoned event does not block a re-import. Deleting an entry and then
            // importing it again is a coherent thing to ask for, and the preview lists
            // every row that will be written, so nothing is resurrected silently.
            foreach (var g in await _glucose.GetForRangeAsync(pet.Id, from, to))
                events.Add(new ExistingEvent(pet.Id, ImportEntryType.Glucose, g.Date.Date, g.Time, g.Value));

            foreach (var s in await _seizures.GetForRangeAsync(pet.Id, from, to))
                events.Add(new ExistingEvent(pet.Id, ImportEntryType.Seizure, s.Date.Date, s.Time, s.DurationSeconds ?? 0));

            foreach (var a in await _appetite.GetAmountsForRangeAsync(pet.Id, from, to))
                events.Add(new ExistingEvent(pet.Id, ImportEntryType.AppetiteAmount, a.Date.Date, a.Time, a.Grams));

            foreach (var w in await _water.GetAmountsForRangeAsync(pet.Id, from, to))
                events.Add(new ExistingEvent(pet.Id, ImportEntryType.WaterAmount, w.Date.Date, w.Time, w.AmountMl));

            foreach (var c in await _custom.GetForRangeAsync(pet.Id, from, to))
                events.Add(new ExistingEvent(pet.Id, ImportEntryType.Custom, c.Date.Date, c.Time, c.Amount ?? 0m, c.CustomTrackerId));
        }

        return new ImportSnapshot
        {
            Pets = existing,
            Trackers = trackers,
            PetDays = petDays,
            LevelDays = levelDays,
            Events = events,
        };
    }

    /// <summary>The span the file covers, read leniently: an unreadable date is the
    /// validator's problem to report, not a reason to fail to look anything up.</summary>
    private static (DateTime From, DateTime To)? DateRangeOf(List<ImportPet> blocks)
    {
        DateTime? min = null, max = null;

        foreach (var entry in blocks.SelectMany(b => b.Entries ?? new List<ImportEntry>()))
        {
            if (!DateTime.TryParseExact(
                    (entry.Date ?? string.Empty).Trim(),
                    ImportFormat.DateFormat,
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None,
                    out var date))
            {
                continue;
            }

            if (min is null || date < min) min = date;
            if (max is null || date > max) max = date;
        }

        return min is null ? null : (min.Value.Date, max!.Value.Date);
    }

    // ── Commit ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Write the plan. All of it, or none of it.
    /// </summary>
    /// <param name="rawText">The file's text, remembered as a hash so a second import of
    /// the identical file can say so. Optional.</param>
    public async Task<ImportResult> CommitAsync(ImportPlan plan, string? rawText = null)
    {
        if (!plan.IsValid)
            throw new InvalidOperationException("Refusing to commit an import plan that failed validation.");

        var result = new ImportResult();

        await _db.Connection.RunInTransactionAsync(conn =>
        {
            foreach (var pet in plan.Pets)
            {
                var petId = ResolveOrCreatePet(conn, pet, result);
                var trackerIds = WriteTrackers(conn, pet, petId, result);

                WriteDays(conn, pet, petId, result);
                WriteLevels(conn, pet, petId, result);
                WriteEvents(conn, pet, petId, result);
                WriteCustomEntries(conn, pet, petId, trackerIds, result);
            }
        });

        if (rawText is not null)
            await RememberAsync(rawText);

        // No "changed" event: the importer is a pushed page, so popping back to a tab
        // runs its OnAppearing and reloads it. An event here would have no subscriber.
        return result;
    }

    private static int ResolveOrCreatePet(SQLiteConnection conn, PlannedPet planned, ImportResult result)
    {
        if (planned.NewPet is null)
            return planned.ExistingPetId;

        var spec = planned.NewPet;
        var pet = new Pet
        {
            Name = spec.Name,
            Type = spec.Species,
            BirthYear = spec.BirthYear,
            BirthMonth = spec.BirthMonth,
            BirthDay = spec.BirthDay,

            // The legacy single-condition column, kept in step with the PetCondition rows
            // below. PetConditionService folds it into a row on first read for a pet that
            // has none, so a seeded pet disagreeing with itself would migrate a duplicate
            // (the same care DemoModeService takes).
            ConditionId = spec.ConditionIds.FirstOrDefault() ?? string.Empty,
        };

        // Snapshot the derived age into the legacy column, exactly as the create form
        // does; AgeYears stays the live, birthday-derived source.
        pet.Age = pet.AgeYears ?? 0;

        conn.Insert(SyncStamp.Touch(pet));

        foreach (var conditionId in spec.ConditionIds)
        {
            conn.Insert(SyncStamp.Touch(new PetCondition
            {
                PetId = pet.Id,
                ConditionId = conditionId,
            }));
        }

        // The care plan is NOT seeded here. CarePlanService seeds it once, lazily, on the
        // first read for the pet, so an imported pet gets exactly the plan its conditions
        // would have given it, through the same path every other pet uses.

        result.PetsCreated++;
        result.FirstNewPetId = result.FirstNewPetId == 0 ? pet.Id : result.FirstNewPetId;
        return pet.Id;
    }

    /// <summary>Insert the definitions this file introduces and return every ref's real
    /// row id, reused ones included.</summary>
    private static Dictionary<string, int> WriteTrackers(
        SQLiteConnection conn, PlannedPet planned, int petId, ImportResult result)
    {
        var ids = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var tracker in planned.Trackers)
        {
            if (!tracker.IsNew)
            {
                ids[tracker.Ref] = tracker.ExistingId;
                continue;
            }

            var definition = tracker.Definition!;
            definition.PetId = petId;
            conn.Insert(SyncStamp.Touch(definition));

            ids[tracker.Ref] = definition.Id;
            result.TrackersCreated++;
        }

        return ids;
    }

    /// <summary>
    /// The day rows: mood and weight.
    ///
    /// <para>This is the only write here that is a MERGE rather than an insert. The row is
    /// one per pet per day and holds two independent halves, so only the halves this
    /// import is actually writing are touched; a mood already on the row survives a
    /// weight-only import untouched. The app's own mood sheet writes the same way.</para>
    /// </summary>
    private static void WriteDays(SQLiteConnection conn, PlannedPet planned, int petId, ImportResult result)
    {
        foreach (var day in planned.Days)
        {
            var row = day.RowId != 0 ? conn.Find<PetEntry>(day.RowId) : null;

            if (row is null)
            {
                row = new PetEntry { PetId = petId, Date = day.Date };
            }
            else if (day.ReviveTombstone)
            {
                // Bring the soft-deleted row back rather than inserting a sibling beside
                // it: the cloud keys this table by (pet, day).
                row.IsDeleted = false;
            }

            if (day.HasMood)
            {
                row.MoodLevel = day.MoodLevel!.Value;

                // The stored Mood string is the display name, which is what the mood sheet
                // writes; MoodLevel is the canonical value. Resolved here rather than in
                // the validator so the pure layer never reads a translated string.
                row.Mood = ((MoodLevel)day.MoodLevel.Value).GetDisplayName();
                row.MoodNote = day.MoodNote;
                row.IncludeInVetReport = day.IncludeNoteInVetReport;
                row.MoodTimeTicks = day.MoodTimeTicks;
                result.EntriesWritten++;
            }

            if (day.HasWeight)
            {
                row.Weight = day.Weight!.Value;
                row.WeightUnit = day.WeightUnit;
                row.WeightTimeTicks = day.WeightTimeTicks;
                result.EntriesWritten++;
            }

            if (row.Id == 0)
                conn.Insert(SyncStamp.Touch(row));
            else
                conn.Update(SyncStamp.Touch(row));
        }
    }

    private static void WriteLevels(SQLiteConnection conn, PlannedPet planned, int petId, ImportResult result)
    {
        foreach (var level in planned.AppetiteLevels)
        {
            if (level.IsRevival && conn.Find<AppetiteEntry>(level.UpdateRowId) is { } row)
            {
                row.Time = level.Row.Time;
                row.Level = level.Row.Level;
                row.Food = level.Row.Food;
                row.IsDeleted = false;
                conn.Update(SyncStamp.Touch(row));
            }
            else
            {
                level.Row.PetId = petId;
                conn.Insert(SyncStamp.Touch(level.Row));
            }

            result.EntriesWritten++;
        }

        foreach (var level in planned.WaterLevels)
        {
            if (level.IsRevival && conn.Find<WaterLevelEntry>(level.UpdateRowId) is { } row)
            {
                row.Time = level.Row.Time;
                row.Level = level.Row.Level;
                row.IsDeleted = false;
                conn.Update(SyncStamp.Touch(row));
            }
            else
            {
                level.Row.PetId = petId;
                conn.Insert(SyncStamp.Touch(level.Row));
            }

            result.EntriesWritten++;
        }
    }

    private static void WriteEvents(SQLiteConnection conn, PlannedPet planned, int petId, ImportResult result)
    {
        foreach (var row in planned.Glucose)
        {
            row.PetId = petId;
            conn.Insert(SyncStamp.Touch(row));
            result.EntriesWritten++;
        }

        foreach (var row in planned.AppetiteAmounts)
        {
            row.PetId = petId;
            conn.Insert(SyncStamp.Touch(row));
            result.EntriesWritten++;
        }

        foreach (var row in planned.WaterAmounts)
        {
            row.PetId = petId;
            conn.Insert(SyncStamp.Touch(row));
            result.EntriesWritten++;
        }

        foreach (var row in planned.Seizures)
        {
            row.PetId = petId;
            conn.Insert(SyncStamp.Touch(row));
            result.EntriesWritten++;
        }
    }

    private static void WriteCustomEntries(
        SQLiteConnection conn, PlannedPet planned, int petId, Dictionary<string, int> trackerIds, ImportResult result)
    {
        foreach (var entry in planned.CustomEntries)
        {
            // Every ref was resolved during validation, so a miss here is a bug rather
            // than bad input, and silently dropping the entry would be the worst way to
            // find out.
            if (!trackerIds.TryGetValue(entry.TrackerRef, out var trackerId))
                throw new InvalidOperationException($"Import plan referenced an unresolved tracker \"{entry.TrackerRef}\".");

            entry.Row.PetId = petId;
            entry.Row.CustomTrackerId = trackerId;
            conn.Insert(SyncStamp.Touch(entry.Row));
            result.EntriesWritten++;
        }
    }

    // ── The repeat-file guard ───────────────────────────────────────────────────
    //
    // Device-scoped, in AppSettings, because it is a fact about this device's import
    // history and nothing else, no medical content, nothing to sync, and a data reset
    // wipes it with everything else.
    //
    // It only catches a BYTE-IDENTICAL re-import. An AI asked the same question twice
    // never produces identical bytes, so this is a convenience, not the defence: the
    // defence is the content rules in ImportValidator, which make a regenerated file
    // land as a no-op.

    private const string SeenKey = "Import:SeenHashes";

    /// <summary>How many file hashes to remember. Enough to cover a testing session;
    /// this is a hint to the owner, not an audit log.</summary>
    private const int MaxRemembered = 20;

    private async Task<bool> WasSeenBeforeAsync(string text)
    {
        var stored = await _settings.GetValueAsync(SeenKey);
        return !string.IsNullOrEmpty(stored) && stored.Split(',').Contains(HashOf(text));
    }

    private async Task RememberAsync(string text)
    {
        try
        {
            var hash = HashOf(text);
            var stored = await _settings.GetValueAsync(SeenKey) ?? string.Empty;
            var hashes = stored.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

            hashes.Remove(hash);
            hashes.Insert(0, hash);

            await _settings.SetValueAsync(SeenKey, string.Join(',', hashes.Take(MaxRemembered)));
        }
        catch (Exception ex)
        {
            // A failure here loses a convenience, never data. The import already
            // committed; refusing to acknowledge it would be the worse outcome.
            Debug.WriteLine($"[Import] could not record file hash: {ex.Message}");
        }
    }

    private static string HashOf(string text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.Trim()))).ToLowerInvariant();
}

/// <summary>What a committed import actually did: the numbers the success screen states.
/// Counted during the write rather than from the plan, so they describe what happened.</summary>
public sealed class ImportResult
{
    public int PetsCreated { get; set; }
    public int TrackersCreated { get; set; }
    public int EntriesWritten { get; set; }

    /// <summary>The first pet this import created, or 0. The importer offers to open it,
    /// because a new pet the owner cannot see is indistinguishable from a failure.</summary>
    public int FirstNewPetId { get; set; }
}
