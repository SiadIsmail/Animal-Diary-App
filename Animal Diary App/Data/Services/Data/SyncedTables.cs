namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using SQLite;

// PetScope and the WHERE fragments built from it live in PetScopeSql.cs — pure strings,
// compile-linked into the test project so the demo-exclusion guard can be proven.

/// <summary>Any table this app creates — synced or device-local. The two lifecycle
/// steps every table shares, expressed without naming its type.</summary>
public abstract class AppTable
{
    public abstract Type EntityType { get; }

    /// <summary>The local SQLite table name. sqlite-net maps a class to its own
    /// name unless told otherwise, and nothing here overrides that.</summary>
    public string LocalTable => EntityType.Name;

    /// <summary>Create the table on an already-open synchronous connection. Every
    /// table is created inside ONE transaction at startup (see AppDatabase), so this
    /// is the form that path uses; the async form below is not otherwise needed.</summary>
    public abstract void CreateTable(SQLiteConnection conn);

    public abstract Task DeleteEveryRowAsync(SQLiteAsyncConnection db);
}

/// <summary>A device-local table: created and wiped, never tombstoned or pushed.</summary>
public sealed class LocalOnlyTable<T> : AppTable where T : new()
{
    public override Type EntityType => typeof(T);
    public override void CreateTable(SQLiteConnection conn) => conn.CreateTable<T>();
    public override Task DeleteEveryRowAsync(SQLiteAsyncConnection db) => db.DeleteAllAsync<T>();
}

/// <summary>
/// One synced table, with everything the lifecycle paths need to operate on it
/// without naming its type. See <see cref="SyncedTables"/> for why this exists.
/// </summary>
public abstract class SyncedTable : AppTable
{
    public abstract PetScope Scope { get; }

    /// <summary>The <c>WHERE</c> fragment selecting this table's rows for one pet,
    /// with a single <c>?</c> parameter bound to the pet's local id.</summary>
    public string PetPredicate => PetScopeSql.Belongs(Scope);

    /// <summary>The <c>WHERE</c> fragment excluding this table's demo rows. Takes no
    /// parameter. Every path that can put a row on the wire must AND this in — see
    /// <see cref="PetScopeSql.ExcludesDemo"/> and <c>Pet.IsDemo</c>.</summary>
    public string ExcludesDemoPredicate => PetScopeSql.ExcludesDemo(Scope);

    public abstract void BackfillSyncColumns(SQLiteConnection conn);

    /// <summary>Every live (non-tombstoned) row belonging to one pet, boxed as
    /// <see cref="ISyncable"/> so callers can stamp and update them without knowing
    /// the type. Used by the soft-delete cascade.</summary>
    public abstract Task<List<ISyncable>> LoadLivePetRowsAsync(SQLiteAsyncConnection db, int petId);
}

/// <summary>The typed half — every member closes over <typeparamref name="T"/>.</summary>
public sealed class SyncedTable<T> : SyncedTable where T : class, ISyncable, new()
{
    public SyncedTable(PetScope scope) => Scope = scope;

    public override Type EntityType => typeof(T);
    public override PetScope Scope { get; }

    public override void CreateTable(SQLiteConnection conn) => conn.CreateTable<T>();

    public override Task DeleteEveryRowAsync(SQLiteAsyncConnection db) => db.DeleteAllAsync<T>();

    public override async Task<List<ISyncable>> LoadLivePetRowsAsync(SQLiteAsyncConnection db, int petId)
    {
        var rows = await db.QueryAsync<T>(
            $"select * from \"{LocalTable}\" where {PetPredicate} and IsDeleted = 0", petId);
        return rows.Cast<ISyncable>().ToList();
    }

    /// <summary>
    /// Normalize the sync columns on rows written before those columns existed.
    ///
    /// <para>NULL breaks both filters (<c>IsDeleted = 0</c> excludes NULL — every old
    /// row would vanish from every read) and identity (no SyncId). Idempotent: after
    /// the first launch the queries match nothing.</para>
    /// </summary>
    public override void BackfillSyncColumns(SQLiteConnection conn)
    {
        var table = conn.GetMapping<T>().TableName;

        // NULL → the columns' pre-sync defaults. UpdatedAtUtc 0 = DateTime.MinValue,
        // which any real edit beats in last-write-wins. IsDirty stays false — the
        // enable-cloud migration marks everything dirty explicitly when the user
        // opts in, so nothing queues for upload before an account exists.
        conn.Execute($"update \"{table}\" set IsDeleted = 0 where IsDeleted is null");
        conn.Execute($"update \"{table}\" set IsDirty = 0 where IsDirty is null");
        conn.Execute($"update \"{table}\" set UpdatedAtUtc = 0 where UpdatedAtUtc is null");

        // Assign the global identity to pre-existing rows. GUIDs must be generated
        // per row in C# (SQLite has no uuid()), but after the first launch this
        // query returns nothing and the whole backfill is a no-op.
        //
        // Demo rows are skipped, and that exclusion is load-bearing rather than an
        // optimization. A seeded demo pet is deliberately left with NO SyncId, because it
        // has no cloud identity and never can have one — and the membership purge reads an
        // empty SyncId as "this pet was never on an account, leave it alone". Without this
        // clause the backfill would hand every demo row a GUID on the very next launch,
        // quietly re-arming that purge against a creator's demo pets. (It would also churn
        // several thousand rows through Update on the launch after seeding.)
        var missing = conn.Query<T>(
            $"select * from \"{table}\" where (SyncId is null or SyncId = '') and {ExcludesDemoPredicate}");
        foreach (var row in missing)
        {
            row.SyncId = Guid.NewGuid().ToString();
            conn.Update(row);
        }
    }
}

/// <summary>
/// <b>The one list of synced tables.</b> Adding a health-data table means adding a
/// line here — and the five lifecycle paths that must know about it all iterate this
/// list instead of repeating it:
///
/// <list type="number">
/// <item><c>AppDatabase.InitAsync</c> — create the table, then backfill its sync columns.</item>
/// <item><c>AppResetService.ResetDataAsync</c> — wipe it (a data reset must leave nothing).</item>
/// <item><c>PetDeletionService.DeletePetAsync</c> — tombstone the pet's rows so the
/// delete reaches the cloud.</item>
/// <item><c>CloudSyncService.PurgePetAsync</c> — hard-delete the rows of a pet whose
/// access was revoked (no tombstone: it must not propagate).</item>
/// <item><c>CloudSyncService</c> dirty-marking / identity re-minting on enable and on
/// an account switch.</item>
/// <item><c>TableSync.CollectDirtyAsync</c> and the two sweeps above — excluding demo
/// rows from every path that can put data on the wire (<c>Pet.IsDemo</c>,
/// <see cref="SyncedTable.ExcludesDemoPredicate"/>).</item>
/// </list>
///
/// <para><b>Why this exists.</b> Those five lists used to be written out by hand, in
/// four different forms, plus the cloud mapping in <c>SyncTableMaps</c> — and every
/// omission was silent. Forget the backfill and old rows vanish from every read;
/// forget the reset and medical data survives a wipe the user asked for; forget the
/// purge and a revoked caregiver keeps another household's records. None of that
/// fails a build or a test. Now a missing line is either a compile error or caught by
/// the mapping check below.</para>
///
/// <para><b>Order is load-bearing:</b> parents first, children after
/// (<c>Pet</c> → <c>Medication</c> → …), so a pull applies FKs in a resolvable order.
/// Deletion walks it <see cref="InDeletionOrder"/> — reversed — so children go before
/// the parents they point at. <c>MedicationSchedule</c> sits after <c>Medication</c>
/// here and therefore before it when reversed, which is what its
/// <see cref="PetScope.ByMedicationId"/> subquery needs.</para>
///
/// <para>Tables that are <b>not</b> synced (<c>ReminderInstance</c>,
/// <c>VetReportFile</c>, <c>AppSettings</c>, <c>SyncState</c>) are device-local and
/// live in <see cref="LocalOnly"/> instead — they are created and wiped, never
/// tombstoned or pushed.</para>
/// </summary>
public static class SyncedTables
{
    /// <summary>Parents before children. See the ordering note above before reordering.</summary>
    public static readonly IReadOnlyList<SyncedTable> All = new SyncedTable[]
    {
        new SyncedTable<Pet>(PetScope.Root),
        new SyncedTable<Medication>(PetScope.ByPetId),
        new SyncedTable<MedicationSchedule>(PetScope.ByMedicationId),
        new SyncedTable<MedicationDoseLog>(PetScope.ByPetId),
        new SyncedTable<PetEntry>(PetScope.ByPetId),
        new SyncedTable<Tracker>(PetScope.ByPetId),
        new SyncedTable<PetCondition>(PetScope.ByPetId),
        new SyncedTable<GlucoseEntry>(PetScope.ByPetId),
        new SyncedTable<AppetiteEntry>(PetScope.ByPetId),
        new SyncedTable<AppetiteAmountEntry>(PetScope.ByPetId),
        new SyncedTable<SeizureEntry>(PetScope.ByPetId),
        new SyncedTable<WaterAmountEntry>(PetScope.ByPetId),
        new SyncedTable<WaterLevelEntry>(PetScope.ByPetId),
        // The definition before its entries — see the ordering note above.
        new SyncedTable<CustomTracker>(PetScope.ByPetId),
        new SyncedTable<CustomEntry>(PetScope.ByPetId),
    };

    /// <summary>Children before parents — the order any delete cascade must use.</summary>
    public static IEnumerable<SyncedTable> InDeletionOrder => All.Reverse();

    private static readonly Dictionary<Type, SyncedTable> ByEntityType =
        All.ToDictionary(t => t.EntityType);

    /// <summary>This entity's registry entry — its pet scope, and the WHERE fragments
    /// built from it. Throws for a type that isn't registered, which is the right
    /// outcome: a synced table missing from <see cref="All"/> is the silent-omission
    /// class of bug this registry exists to make impossible.</summary>
    public static SyncedTable For<T>() where T : class, ISyncable, new() =>
        ByEntityType.TryGetValue(typeof(T), out var table)
            ? table
            : throw new InvalidOperationException(
                $"{typeof(T).Name} is synced but missing from {nameof(SyncedTables)}.{nameof(All)}.");

    /// <summary>
    /// The device-local tables: created and wiped like the rest, but never synced,
    /// so they carry no sync columns and are hard-deleted rather than tombstoned.
    /// Kept here so "every table this app creates" is still answerable from one file.
    /// </summary>
    public static readonly IReadOnlyList<AppTable> LocalOnly = new AppTable[]
    {
        new LocalOnlyTable<AppSettings>(),
        new LocalOnlyTable<ReminderInstance>(),
        new LocalOnlyTable<VetReportFile>(),
        new LocalOnlyTable<SyncState>(),
    };

    /// <summary>Every table the app creates, synced and local-only alike — what
    /// <c>AppDatabase</c> creates and what a data reset must wipe.</summary>
    public static IEnumerable<AppTable> Everything => All.Concat<AppTable>(LocalOnly);
}
