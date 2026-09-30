namespace Animal_Diary_App.Data.Services.Import;

using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  The validated result: exactly what will be written, decided once.
//
//  The preview and the commit read THE SAME object. Nothing is re-derived between
//  showing the owner what will happen and doing it, which is the only way the preview
//  can be a promise rather than an estimate. Re-running validation at commit time would
//  mean the screen and the write could disagree, and the disagreement would be silent.
//
//  Rows are carried as the app's REAL entities, already populated except for PetId
//  (unknown until a new pet is inserted) and the foreign keys resolved at commit. There
//  is no parallel "import row" type: the importer's job is to produce ordinary rows,
//  and an imported entry is indistinguishable from a hand-logged one from the moment it
//  lands: same tables, same sync stamps, same undo, same vet report.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>A pet the file asks to create. Not a <c>Pet</c>: that type reaches into
/// PetPhotoService and cannot be linked into the pure test project (see
/// <see cref="ImportSnapshot"/>), and building the entity is the commit's job anyway.</summary>
public sealed record PlannedNewPet(
    string Name,
    string Species,
    int BirthYear,
    int? BirthMonth,
    int? BirthDay,
    IReadOnlyList<string> ConditionIds);

/// <summary>One custom tracker the file needs, resolved to either an existing row or a
/// definition to create.</summary>
/// <param name="Ref">The file-local handle its entries point at.</param>
/// <param name="ExistingId">The tracker row to reuse, or 0 when one must be created.</param>
/// <param name="Definition">The row to insert when <paramref name="ExistingId"/> is 0;
/// null otherwise.</param>
/// <param name="MatchedArchived">The reused tracker is retired. It stays retired: an
/// import adds history, it does not resume a routine the owner stopped, and the
/// preview says so, because entries landing on an archived tracker are visible in the
/// timeline and the report but absent from the chip row.</param>
/// <param name="Shape">What it records. Carried here rather than reached for through
/// <paramref name="Definition"/>, which is null for a reused tracker: the entry pass
/// needs the shape for BOTH kinds to decide whether an amount is expected.</param>
/// <param name="Ordinal">Position within the pet block. Used only as a stable stand-in
/// identity for a tracker that does not exist yet, so two new trackers ticked at the
/// same minute do not fingerprint as one entry.</param>
public sealed record PlannedTracker(
    string Ref,
    int ExistingId,
    CustomTracker? Definition,
    bool MatchedArchived,
    CustomShape Shape,
    int Ordinal)
{
    public bool IsNew => ExistingId == 0;
}

/// <summary>
/// One day's mood and/or weight: the <c>PetEntry</c> write.
///
/// <para>Not a plain <c>PetEntry</c> because this write is a MERGE. The row is one per
/// pet per day and holds two independent halves, so an import may be filling an empty
/// half of a row that already exists, reviving a tombstone, or inserting fresh, and
/// the halves it is NOT writing must be left exactly as they are. The app's own mood
/// sheet does the same thing ("write just the mood columns, leaving weight untouched").</para>
/// </summary>
public sealed class PlannedPetDay
{
    public required DateTime Date { get; init; }

    /// <summary>The row to update, or 0 to insert a new one.</summary>
    public int RowId { get; init; }

    /// <summary>The target row is soft-deleted and must come back with this write.
    /// Reviving beats inserting a sibling: the cloud keys this table by (pet, day).</summary>
    public bool ReviveTombstone { get; init; }

    // Mood half: all null/default when this day carries only a weight.
    public int? MoodLevel { get; set; }
    public string MoodNote { get; set; } = string.Empty;
    public bool IncludeNoteInVetReport { get; set; }
    public long? MoodTimeTicks { get; set; }

    // Weight half. Weight is CANONICAL kilograms; WeightUnit is provenance, the unit
    // the file said the number was in (AI/domain.md, Units).
    public decimal? Weight { get; set; }
    public string? WeightUnit { get; set; }
    public long? WeightTimeTicks { get; set; }

    public bool HasMood => MoodLevel is not null;
    public bool HasWeight => Weight is not null;
}

/// <summary>A one-per-day row to write: appetite level or water level. The row is
/// complete; <paramref name="UpdateRowId"/> says whether it lands as an update of an
/// existing tombstone (revival) or a fresh insert.</summary>
public sealed record PlannedLevelRow<T>(T Row, int UpdateRowId)
{
    public bool IsRevival => UpdateRowId != 0;
}

/// <summary>A custom entry, still pointing at its tracker by file-local ref: the real
/// <c>CustomTrackerId</c> is not knowable until the definitions are inserted.</summary>
public sealed record PlannedCustomEntry(string TrackerRef, CustomEntry Row);

/// <summary>Everything one pet block will write.</summary>
public sealed class PlannedPet
{
    public required ImportPetMatch Match { get; init; }

    /// <summary>The name as the file gave it: what the preview shows.</summary>
    public required string Name { get; init; }

    /// <summary>The pet to append to, or 0 when <see cref="NewPet"/> is set.</summary>
    public int ExistingPetId { get; init; }

    /// <summary>The pet to create, or null when appending.</summary>
    public PlannedNewPet? NewPet { get; init; }

    public List<PlannedTracker> Trackers { get; } = new();

    public List<PlannedPetDay> Days { get; } = new();
    public List<GlucoseEntry> Glucose { get; } = new();
    public List<PlannedLevelRow<AppetiteEntry>> AppetiteLevels { get; } = new();
    public List<AppetiteAmountEntry> AppetiteAmounts { get; } = new();
    public List<PlannedLevelRow<WaterLevelEntry>> WaterLevels { get; } = new();
    public List<WaterAmountEntry> WaterAmounts { get; } = new();
    public List<SeizureEntry> Seizures { get; } = new();
    public List<PlannedCustomEntry> CustomEntries { get; } = new();

    /// <summary>What the owner should know about this pet before confirming.</summary>
    public List<ImportNotice> Notices { get; } = new();

    /// <summary>How many rows this block writes, counting a merged mood+weight day as
    /// the two entries the file listed rather than the one row they share: the preview
    /// is answering "what happens to my notes", not "how many INSERTs run".</summary>
    public int EntryCount =>
        Days.Sum(d => (d.HasMood ? 1 : 0) + (d.HasWeight ? 1 : 0))
        + Glucose.Count
        + AppetiteLevels.Count
        + AppetiteAmounts.Count
        + WaterLevels.Count
        + WaterAmounts.Count
        + Seizures.Count
        + CustomEntries.Count;

    /// <summary>Trackers this block will create (as opposed to reuse).</summary>
    public int NewTrackerCount => Trackers.Count(t => t.IsNew);

    /// <summary>How many of the file's entries will NOT be written, and why: the
    /// number the preview must show, because it is the difference between what the
    /// owner's file said and what their diary will hold.</summary>
    public int SkippedCount =>
        Notices.Count(n => n.Kind is ImportNoticeKind.SlotOccupied or ImportNoticeKind.AlreadyPresent);

    /// <summary>The span the written entries cover, or null when nothing will be
    /// written. Shown in the preview as the sanity check that catches a mis-transcribed
    /// year better than any bound does: "1924 to 2026" is obvious at a glance.</summary>
    public (DateTime From, DateTime To)? DateRange
    {
        get
        {
            var dates = Days.Select(d => d.Date)
                .Concat(Glucose.Select(g => g.Date))
                .Concat(AppetiteLevels.Select(a => a.Row.Date))
                .Concat(AppetiteAmounts.Select(a => a.Date))
                .Concat(WaterLevels.Select(w => w.Row.Date))
                .Concat(WaterAmounts.Select(w => w.Date))
                .Concat(Seizures.Select(s => s.Date))
                .Concat(CustomEntries.Select(c => c.Row.Date))
                .ToList();
            return dates.Count == 0 ? null : (dates.Min(), dates.Max());
        }
    }
}

/// <summary>
/// The whole validated file: either a set of errors (and nothing else usable) or a plan
/// that can be committed.
/// </summary>
public sealed class ImportPlan
{
    public IReadOnlyList<ImportError> Errors { get; init; } = Array.Empty<ImportError>();

    public IReadOnlyList<PlannedPet> Pets { get; init; } = Array.Empty<PlannedPet>();

    /// <summary>Notices that belong to the file rather than to one pet. Mutable so the
    /// service can add what only it knows, that this exact file has been imported
    /// before: without the pure validator needing a concept of "before".</summary>
    public List<ImportNotice> FileNotices { get; init; } = new();

    /// <summary>The file's own description of where the notes came from, shown in the
    /// preview so two files generated the same day can be told apart.</summary>
    public string? SourceNote { get; init; }

    /// <summary>True when the file may be committed. A single error rejects all of it,
    /// there is no partial-import path (see <see cref="ImportError"/>).</summary>
    public bool IsValid => Errors.Count == 0;

    /// <summary>Every notice, file-level first, then per pet in file order.</summary>
    public IReadOnlyList<ImportNotice> AllNotices =>
        FileNotices.Concat(Pets.SelectMany(p => p.Notices)).ToList();

    public int TotalEntryCount => Pets.Sum(p => p.EntryCount);

    public int TotalSkippedCount => Pets.Sum(p => p.SkippedCount);

    /// <summary>A plan that will write nothing: every row in the file was already
    /// there. Worth naming, because it is what a second import of the same notes should
    /// produce, and the success screen says so rather than claiming an import happened.</summary>
    public bool IsEmpty => TotalEntryCount == 0 && Pets.All(p => p.NewPet is null && p.NewTrackerCount == 0);

    public static ImportPlan Failed(IReadOnlyList<ImportError> errors) => new() { Errors = errors };
}
