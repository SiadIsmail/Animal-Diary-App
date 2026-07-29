namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The tiny persistence seam the trial clock needs: read/write its start instant.
/// Implemented by <c>SettingsService</c> over SQLite in the app; a fake in tests. Keeps
/// <see cref="TrialService"/> free of any SQLite/MAUI dependency so the trial + gate logic
/// is unit-testable in a plain net9.0 assembly (the report layer follows the same rule).
/// </summary>
public interface ITrialStore
{
    /// <summary>The UTC instant the trial began, or null if it has not started.</summary>
    Task<DateTime?> GetTrialStartUtcAsync();

    /// <summary>Persist the trial start instant.</summary>
    Task SetTrialStartUtcAsync(DateTime startUtc);
}

/// <summary>
/// The trial anchor as the cloud layer sees it — the sibling seam to
/// <see cref="IPetAccessSource"/>, pointing the other way. The sync engine reconciles this
/// device's anchor with the account's so that the SERVER can answer "is this owner still
/// in their trial?" when deciding whether they sponsor their caregivers.
///
/// <para><b>Reconciliation is monotone: signing in can only ever move the anchor EARLIER,
/// never later.</b> Otherwise a new email address would hand out a fresh sponsorship
/// window, and a long-time local user whose trial expired would start sponsoring
/// caregivers while locked out of their own app.</para>
/// </summary>
public interface ITrialAnchor
{
    /// <summary>This device's trial start, or null when the trial has never started
    /// (which, per the "trial begins with your first own pet" rule, is the normal state
    /// for someone who only ever cares for other people's animals).
    ///
    /// <para>Async because the sync engine can reach this before billing has initialized;
    /// a synchronous read would see a not-yet-loaded null, report "no trial", and then
    /// never re-claim.</para></summary>
    Task<DateTime?> GetStartUtcAsync();

    /// <summary>Take the account-wide anchor the server returned. Keeps the earlier of the
    /// two; a null server value changes nothing. Idempotent.</summary>
    Task AdoptAsync(DateTime? serverStartUtc);
}
