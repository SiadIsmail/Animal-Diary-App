namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// What the cloud knows about ONE pet's sponsorship, as cached on this device. Handed to
/// <see cref="EntitlementService"/> so it can answer "may I write to this pet?" without
/// knowing anything about Supabase, memberships, or HTTP.
/// </summary>
/// <param name="IsCaregiver">True only when the caller is a <i>caregiver</i> on this pet.
/// The owner's own pets are never sponsored — their access is their own, which is the
/// structural reason one subscription cannot be shared into unlimited free accounts.</param>
/// <param name="OwnerHasAccess">Whether the pet's owner had access (subscription or
/// trial) as of <paramref name="FetchedUtc"/>. Computed server-side; the client never
/// derives it.</param>
/// <param name="FetchedUtc">When this record was last confirmed with the server. Drives
/// the offline grace window — a caregiver on a plane keeps working.</param>
public sealed record PetAccessInfo(bool IsCaregiver, bool OwnerHasAccess, DateTime FetchedUtc);

/// <summary>
/// The seam through which billing reads cloud sponsorship — the sibling of
/// <see cref="ITrialStore"/>, and for the same reason: it keeps
/// <see cref="EntitlementService"/> free of any MAUI/SQLite/cloud dependency so the gate
/// logic stays unit-testable in the plain net9.0 test assembly. Implemented by the cloud
/// sync engine (which owns the cache) and by its Null counterpart.
///
/// <para><b>Dependency direction is deliberate:</b> Billing declares this interface and
/// Cloud implements it, never the reverse. Billing must not learn about Supabase.</para>
/// </summary>
public interface IPetAccessSource
{
    /// <summary>False ONLY while a signed-in, backup-enabled device is still waiting for
    /// its first access fetch. While false the gate stays open, mirroring
    /// <see cref="IStoreBilling.EntitlementKnown"/> — a caregiver must not be locked out
    /// in the seconds between redeeming an invite and the first sync landing.
    ///
    /// <para><b>Signed out, backup off, or cloud disabled must report <c>true</c></b>
    /// ("nothing to wait for"), exactly as <c>NullStoreBilling</c> does. Reporting false
    /// there would hold the gate permanently open and the read-only state would never
    /// engage for local-only users.</para></summary>
    bool AccessKnown { get; }

    /// <summary>The cached sponsorship record for a pet, or null when the pet is unknown,
    /// not shared, or locally owned. Never throws; never blocks.</summary>
    PetAccessInfo? GetPetAccess(string? petSyncId);
}

/// <summary>Registered wherever there is no cloud. Nothing to wait for, nothing
/// sponsored — so access falls back entirely to the local trial/subscription.</summary>
public sealed class NullPetAccessSource : IPetAccessSource
{
    public bool AccessKnown => true;
    public PetAccessInfo? GetPetAccess(string? petSyncId) => null;
}
