namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// The app's single analytics boundary. Every feature talks to <b>this</b> — never
/// to PostHog directly — exactly like the notification subsystem talks to
/// <see cref="Data.Device.INotificationService"/>. That indirection is what lets us
/// disable analytics, swap PostHog for another backend, or add a consent gate later
/// without editing a single feature file.
///
/// Contract for every implementation:
/// <list type="bullet">
///   <item>Calls are <b>fire-and-forget and non-blocking</b> — <see cref="Track"/>
///   returns immediately and never blocks the UI thread.</item>
///   <item>Calls <b>never throw</b>. Analytics is best-effort telemetry; a network
///   failure or a serialization bug must degrade to a logged no-op, never a crash.</item>
///   <item>Only the event name and the caller-supplied properties are sent. No
///   personal data, names, notes, medical detail, location, or device fingerprint is
///   ever added by the implementation (see the analytics docs for the full list).</item>
/// </list>
/// </summary>
public interface IAnalyticsService
{
    /// <summary>True when events are actually being sent. False when analytics is
    /// disabled (master switch off, or no project key configured) — callers don't
    /// need to check it, but it makes the disabled state observable/testable.</summary>
    bool IsEnabled { get; }

    /// <summary>Prepare the pipeline (resolve/create the anonymous id, build the HTTP
    /// client). Safe to call once at startup; idempotent.</summary>
    Task InitializeAsync();

    /// <summary>
    /// Record a product event. <paramref name="eventName"/> must be one of the
    /// snake_case constants in <see cref="AnalyticsEvents"/>. <paramref name="properties"/>
    /// describe the <i>event</i> (e.g. species, entry_type) — never the user. Pass a
    /// dictionary keyed by the property constants in <see cref="AnalyticsEvents"/>.
    /// </summary>
    void Track(string eventName, IReadOnlyDictionary<string, object?>? properties = null);
}
