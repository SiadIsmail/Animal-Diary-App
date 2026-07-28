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
