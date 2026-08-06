namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The third access source: full access <i>granted</i> by a redeemed access code,
/// running until a date. Not a purchase and not a trial — nothing is charged,
/// nothing auto-renews, and there is nothing to cancel. Redeeming a second code
/// renews the window from that moment rather than queueing behind the first
/// (the arithmetic lives server-side, in <c>redeem_access_code</c>).
///
/// <para>The sibling seam of <see cref="ITrialStore"/> and <see cref="IPetAccessSource"/>,
/// and for the same reason: <b>Billing declares it, Cloud implements it, never the
/// reverse.</b> Keeping the interface here is what lets the gate logic stay in a plain
/// net10.0 test assembly with no MAUI, SQLite or Supabase anywhere near it.</para>
///
/// <para><b>Why this one owns a <see cref="RefreshAsync"/> when <see cref="IPetAccessSource"/>
/// does not.</b> The sponsorship cache rides the sync cycle, and <c>SyncNowAsync</c> returns
/// early with <c>BackupDisabled</c> when backup is off. A grant has to reach someone who
/// signed in <i>only</i> to redeem a code and never turned backup on, so it cannot depend on
/// that cycle and fetches on its own.</para>
/// </summary>
public interface IGrantSource
{
    /// <summary>False ONLY while a signed-in device still owes its first grant fetch.
    /// Signed out, cloud disabled, or already fetched (even to "no grant") must report
    /// <c>true</c> — "nothing to wait for" — exactly as
    /// <see cref="IPetAccessSource.AccessKnown"/> and <see cref="IStoreBilling.EntitlementKnown"/>
    /// do. Reporting false where there is nothing to wait for holds the gate permanently
    /// open and the read-only state never engages.</summary>
    bool GrantKnown { get; }

    /// <summary>Whether a redeemed grant is currently running. Pure, synchronous and
    /// local: read from a cached absolute instant, never a network call. Because the
    /// instant is absolute, a grant needs <b>no offline grace window</b> the way
    /// sponsorship does — it expires by itself, on the device. Never throws.</summary>
    bool IsGranted { get; }

    /// <summary>When the running grant ends, for copy only (never as the gate — read
    /// <see cref="IsGranted"/> for that). Null when there is no grant.</summary>
    DateTime? GrantedUntilUtc { get; }

    /// <summary>Whether this account has EVER held a grant, including an expired one.
    /// The guard that stops a lapsed year-long grant from being reported as a lapsed
    /// 14-day trial: most granted owners did start a trial once, so
    /// <see cref="IEntitlementService.TrialEverStarted"/> does not protect that copy.</summary>
    bool EverGranted { get; }

    /// <summary>Re-read the grant from the server when possible. Called from
    /// <see cref="IEntitlementService.RefreshAsync"/>, i.e. on launch and resume, so
    /// there is no separate trigger to maintain. Non-throwing: a failure leaves the
    /// cached value in place, which is what keeps a granted user working offline.</summary>
    Task RefreshAsync();
}

/// <summary>Registered wherever there is no cloud (cloud disabled, or the Null billing
/// boundary). Nothing to wait for and nothing granted, so access falls back entirely to
/// the trial and the store.</summary>
public sealed class NullGrantSource : IGrantSource
{
    public bool GrantKnown => true;
    public bool IsGranted => false;
    public DateTime? GrantedUntilUtc => null;
    public bool EverGranted => false;
    public Task RefreshAsync() => Task.CompletedTask;
}
