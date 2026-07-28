namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// Owns the no-card, app-side free trial: when it started and whether it is still
/// running. Deliberately the <i>only</i> place the trial mechanism lives, so a future
/// switch to a store-native trial is a one-service change (see
/// <see cref="IEntitlementService"/>). Trial length is a single editable config value
/// (<see cref="BillingConfig.TrialLength"/>), never a literal here.
///
/// <para>The start instant is persisted in the app's settings table (survives app
/// restarts; reset by a reinstall/clear-data — an accepted limitation, since a
/// reinstall also wipes the owner's pet data). All times are UTC.</para>
/// </summary>
public sealed class TrialService
{
    private readonly ITrialStore _settings;
    private readonly Func<DateTime> _utcNow;
    private DateTime? _startUtc;
    private bool _loaded;

    /// <param name="settings">Persistence for the trial start instant (SQLite in the app,
    /// a fake in tests) — an <see cref="ITrialStore"/> so this class stays MAUI/SQLite-free.</param>
    /// <param name="utcNow">Clock, injectable so trial-expiry can be unit-tested. Defaults
    /// to the system UTC clock. NB: because the clock is the device's, a user setting it
    /// back extends the trial — an accepted limitation for a non-aggressive, no-account
    /// trial (a reinstall also wipes their data, so gaming it is self-defeating).</param>
    public TrialService(ITrialStore settings, Func<DateTime>? utcNow = null)
    {
        _settings = settings;
        _utcNow = utcNow ?? (() => DateTime.UtcNow);
    }

    /// <summary>Load the persisted start instant. Idempotent; call once at startup.</summary>
    public async Task InitializeAsync()
    {
        if (_loaded)
            return;
        _startUtc = await _settings.GetTrialStartUtcAsync();
        _loaded = true;
    }

    /// <summary>The trial has begun and its window has not yet elapsed.</summary>
    public bool IsActive =>
        _startUtc is DateTime start && _utcNow() < start + BillingConfig.TrialLength;

    /// <summary>Whole days remaining in the trial (rounded up), or 0 once elapsed /
    /// never started. Used only for the pre-end nudge copy.</summary>
    public int DaysLeft
    {
        get
        {
            var remaining = TimeRemaining;
            return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalDays);
        }
    }

    /// <summary>Exact time left in the trial, or <see cref="TimeSpan.Zero"/> once elapsed
    /// / never started.</summary>
    public TimeSpan TimeRemaining
    {
        get
        {
            if (_startUtc is not DateTime start)
                return TimeSpan.Zero;
            var remaining = (start + BillingConfig.TrialLength) - _utcNow();
            return remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining;
        }
    }

    /// <summary>Start the clock if it has not started. Returns true only on the launch
    /// that actually begins the trial, so the caller can fire <c>trial_started</c> once.
    /// Idempotent — safe to call on every entry into the main app.</summary>
    public async Task<bool> EnsureStartedAsync()
    {
        if (!_loaded)
            await InitializeAsync();
        if (_startUtc.HasValue)
            return false;

        var now = _utcNow();
        _startUtc = now;
        await _settings.SetTrialStartUtcAsync(now);
        return true;
    }
}
