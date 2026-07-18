namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// A no-op <see cref="IAnalyticsService"/>. Registered instead of the PostHog
/// implementation when analytics is turned off (<see cref="AnalyticsConfig.Enabled"/>
/// is false, or no project key is configured). Its existence keeps "disable
/// analytics" to a one-line DI decision — every call site stays identical and simply
/// does nothing.
/// </summary>
public sealed class NullAnalyticsService : IAnalyticsService
{
    public bool IsEnabled => false;

    public Task InitializeAsync() => Task.CompletedTask;

    public void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null)
    {
        // Intentionally nothing.
    }
}
