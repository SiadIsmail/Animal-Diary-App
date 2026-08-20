namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// Persistence for the offline analytics queue — one blob in, one blob out. Mirrors
/// <see cref="Billing.IPetAccessSource"/>: the narrow seam exists so
/// <see cref="AnalyticsEventQueue"/> holds no MAUI/file-system types and can be unit
/// tested against an in-memory fake.
///
/// Implementations must never throw; a storage failure degrades to "no queue" (events are
/// dropped), never to a crash. Analytics is best-effort by contract.
/// </summary>
public interface IAnalyticsQueueStore
{
    /// <summary>The persisted blob, or null when nothing has been stored yet.</summary>
    Task<string?> ReadAsync();

    /// <summary>Replace the persisted blob.</summary>
    Task WriteAsync(string contents);
}
