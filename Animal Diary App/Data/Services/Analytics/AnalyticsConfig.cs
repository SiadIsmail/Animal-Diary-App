namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// Compile-time configuration for the analytics pipeline. Kept in one place so the
/// destination, the credential, and the master on/off switch are all obvious and
/// changeable without touching feature code.
///
/// The <see cref="ProjectApiKey"/> is a PostHog <b>project</b> key (the "phc_…"
/// write key). Unlike a personal API key it is designed to ship inside client apps:
/// it can only <i>send</i> events, never read data, so embedding it here is the
/// standard, intended usage — not a secret leak.
///
/// The region is deliberately EU (<c>eu.i.posthog.com</c>): a privacy-first,
/// GDPR-friendly posture keeps event data inside the EU. Change the host only if the
/// PostHog project itself lives in another region (the website and app must share the
/// same project/region for future funnel analysis — see the analytics docs).
/// </summary>
public static class AnalyticsConfig
{
    /// <summary>
    /// Master switch. When false the app resolves a no-op analytics implementation
    /// and nothing is ever collected or sent. This is the single line to flip to
    /// disable analytics entirely (e.g. for a build, a region, or a future
    /// consent gate).
    /// </summary>
    public const bool Enabled = true;

    /// <summary>PostHog project ("phc_…") key. If left blank, analytics self-disables
    /// at runtime even when <see cref="Enabled"/> is true — no key, no traffic.</summary>
    public const string ProjectApiKey = "phc_AVyqnTNmajGz4mLXyETeaGkFGFJbxGg7WyAyzBJtaVYR";

    /// <summary>PostHog ingestion host. EU cloud by default for data residency.</summary>
    public const string Host = "https://eu.i.posthog.com";
}
