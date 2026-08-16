namespace Animal_Diary_App.Data.Services;

/// <summary>How a table's rows are reached from a pet — the shape of the WHERE
/// clause that selects "everything belonging to pet N".</summary>
public enum PetScope
{
    /// <summary>The pet row itself, keyed by its own primary key.</summary>
    Root,

    /// <summary>Has a direct <c>PetId</c> column (most tables).</summary>
    ByPetId,

    /// <summary>Hangs off a medication, which hangs off the pet
    /// (<c>MedicationSchedule</c>).</summary>
    ByMedicationId,
}

/// <summary>
/// The two WHERE fragments every synced table needs, one per <see cref="PetScope"/>:
/// "belongs to this pet", and "does NOT belong to a demo pet".
///
/// <para>Split out of <c>SyncedTables</c> for one reason: <see cref="ExcludesDemo"/> is
/// the guard that stops a seeded demo history from ever reaching someone's account, and
/// a guard that cannot be tested is a guard nobody can trust. Everything here is a plain
/// string built from an enum, so this file is compile-linked into the test project while
/// <c>SyncedTables</c> — which names all fifteen entity types — is not.</para>
/// </summary>
public static class PetScopeSql
{
    /// <summary>The set of demo pets, as a subquery. The single source of truth for what
    /// "demo" means: <c>Pet.IsDemo</c>, and nothing else anywhere carries the flag.</summary>
    public const string DemoPetIds = "select Id from \"Pet\" where IsDemo = 1";

    /// <summary>Rows belonging to one pet, with a single <c>?</c> parameter bound to the
    /// pet's local id.</summary>
    public static string Belongs(PetScope scope) => scope switch
    {
        PetScope.Root => "Id = ?",
        PetScope.ByPetId => "PetId = ?",
        PetScope.ByMedicationId =>
            "MedicationId in (select Id from \"Medication\" where PetId = ?)",
        _ => throw new NotSupportedException($"Unhandled {nameof(PetScope)}: {scope}"),
    };

    /// <summary>
    /// Rows that belong to no demo pet. Takes no parameter — it is a standing condition,
    /// not a query about one animal.
    ///
    /// <para><b>All three arms are the same shape</b> — <c>not in (the demo pet set)</c> —
    /// and that uniformity is deliberate rather than tidy. The obvious spelling for the
    /// root case is <c>IsDemo = 0</c>, and it is wrong: <c>IsDemo</c> is an additive column,
    /// so every pet row written before it existed holds NULL, and <c>NULL = 0</c> is not
    /// true. That one clause would have silently dropped every pre-existing pet out of the
    /// upload queue — the exact NULL trap the sync columns' own backfill exists to avoid
    /// (see <c>SyncedTable.BackfillSyncColumns</c>). Phrased as a set membership test, an
    /// unknown flag simply fails to join the demo set and the row syncs, which is the
    /// correct reading of "we don't know" for data somebody already owns.</para>
    ///
    /// <para>The subquery selects a primary key, so it can never yield the NULL that would
    /// make <c>NOT IN</c> collapse to no rows at all.</para>
    /// </summary>
    public static string ExcludesDemo(PetScope scope) => scope switch
    {
        PetScope.Root => $"Id not in ({DemoPetIds})",
        PetScope.ByPetId => $"PetId not in ({DemoPetIds})",
        PetScope.ByMedicationId =>
            $"MedicationId not in (select Id from \"Medication\" where PetId in ({DemoPetIds}))",
        _ => throw new NotSupportedException($"Unhandled {nameof(PetScope)}: {scope}"),
    };
}
