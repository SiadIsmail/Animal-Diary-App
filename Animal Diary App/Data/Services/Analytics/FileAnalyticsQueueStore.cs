namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// File-backed <see cref="IAnalyticsQueueStore"/>: a single JSON file in the app's private
/// data directory.
///
/// <para><b>Why a file and not the app database.</b> Analytics has no dependency on
/// <c>AppDatabase</c> today, and that separation is worth keeping — a telemetry buffer has
/// no business in the schema the owner's pet data lives in, and it would otherwise need a
/// migration. It is also not <c>Preferences</c>: that store is for small scalar settings,
/// and a queue of up to <see cref="AnalyticsEventQueue.MaxEvents"/> payloads is neither
/// small nor scalar.</para>
///
/// <para>The file holds only payloads that were already built for transmission — the same
/// coarse, anonymous fields documented in the analytics contract. Nothing personal is
/// written to disk here that was not already going to be sent. It lives in
/// <see cref="FileSystem.AppDataDirectory"/>, which is private to the app and removed on
/// uninstall.</para>
/// </summary>
public sealed class FileAnalyticsQueueStore : IAnalyticsQueueStore
{
    private const string FileName = "analytics-queue.json";

    private static string Path =>
        System.IO.Path.Combine(FileSystem.AppDataDirectory, FileName);

    public async Task<string?> ReadAsync()
    {
        try
        {
            var path = Path;
            if (!File.Exists(path))
                return null;

            return await File.ReadAllTextAsync(path).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Unreadable buffer is not an error worth surfacing — telemetry degrades.
            System.Diagnostics.Debug.WriteLine($"[Analytics] queue read failed: {ex.Message}");
            return null;
        }
    }

    public async Task WriteAsync(string contents)
    {
        try
        {
            await File.WriteAllTextAsync(Path, contents).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Analytics] queue write failed: {ex.Message}");
        }
    }
}
