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
    private readonly SettingsService _settings;
    private DateTime? _startUtc;
    private bool _loaded;

    public TrialService(SettingsService settings) => _settings = settings;

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
        _startUtc is DateTime start && DateTime.UtcNow < start + BillingConfig.TrialLength;

    /// <summary>Whole days remaining in the trial (rounded up), or 0 once elapsed /
    /// never started. Used only for the pre-end nudge copy.</summary>
    public int DaysLeft
    {
        get
        {
            if (_startUtc is not DateTime start)
                return 0;
            var remaining = (start + BillingConfig.TrialLength) - DateTime.UtcNow;
            return remaining <= TimeSpan.Zero ? 0 : (int)Math.Ceiling(remaining.TotalDays);
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

        var now = DateTime.UtcNow;
        _startUtc = now;
        await _settings.SetTrialStartUtcAsync(now);
        return true;
    }
}
