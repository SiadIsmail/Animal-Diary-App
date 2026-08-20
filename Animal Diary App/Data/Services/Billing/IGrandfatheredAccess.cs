namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// What this install already had on the day the paid boundary moved, and therefore keeps.
///
/// <para><b>Why this exists.</b> Inverting the paywall made logging free forever and made
/// the accumulated record the product. Two things that were free before the change are
/// paid after it, and both were already switched on for real people:</para>
/// <list type="bullet">
///   <item><b>Cloud backup</b> was ungated. Someone with it on would have lost sync.</item>
///   <item><b>A caregiver riding a non-paying owner.</b> The server's
///   <c>owner_has_access</c> lost its trial arm in migration 0021, so a caregiver helping
///   with a free owner's animal would have quietly dropped out of everything that reads
///   sponsorship.</item>
/// </list>
///
/// <para>Neither loses data — the local copy is untouched and reading is never gated —
/// but both are real regressions for real users, so both are grandfathered: whatever this
/// install already had on the first launch after the change, it keeps. The new boundary
/// applies to everyone from there on.</para>
///
/// <para><b>This is device-scoped, and a reinstall loses it.</b> Deliberately, and not
/// worth an account-scoped mechanism: the affected population is tiny (RevenueCat was on
/// the Test Store key, so no purchase has ever completed), and the failure mode is "asked
/// to subscribe", never data loss.</para>
///
/// <para>The seam is pure — declared by Billing, implemented over the settings table in
/// the app — for the same reason <see cref="IPetAccessSource"/> and
/// <see cref="IGrantSource"/> are: the gate stays unit-testable with no MAUI or SQLite.</para>
/// </summary>
public interface IGrandfatheredAccess
{
    /// <summary>Cloud backup was already on when the boundary moved, so it stays on
    /// without a subscription.</summary>
    bool BackupIncluded { get; }

    /// <summary>This device was already a caregiver on this pet when the boundary moved,
    /// so it keeps riding that owner regardless of what the owner pays. Scoped to the pets
    /// that were actually there: a blanket "this device is grandfathered" boolean would
    /// also cover every pet joined afterwards, which is a different promise from keeping
    /// what you had.</summary>
    bool SponsorshipIncluded(string? petSyncId);
}

/// <summary>Nothing grandfathered — the state of every install created after the boundary
/// moved, and of every platform without the settings table (desktop dev, tests).</summary>
public sealed class NullGrandfatheredAccess : IGrandfatheredAccess
{
    public bool BackupIncluded => false;
    public bool SponsorshipIncluded(string? petSyncId) => false;
}
