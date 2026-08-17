namespace Animal_Diary_App.Data.Services.Data.Device;

/// <summary>
/// The platform boundary for "how did this install arrive". On Android that is Google
/// Play's install referrer, preserved through an install from a link like
/// <c>…/store/apps/details?id=com.felova.app&amp;referrer=creator%3Dtheto</c> and readable
/// once the app first runs.
///
/// <para><b>Android only, and that is a platform fact rather than a gap.</b> Apple's
/// campaign tokens (<c>pt</c>/<c>ct</c>) reach App Store Connect analytics and are not
/// readable by the app at runtime, and AdServices covers Apple Search Ads rather than
/// arbitrary links. Every other platform returns null, and the typed code stays the only
/// path there.</para>
///
/// <para>Sits beside <see cref="INotificationService"/> for the same reason: the caller
/// wants a string, not a Java service connection, and the cloud/referral layer must stay
/// free of platform types.</para>
/// </summary>
public interface IInstallReferrerSource
{
    /// <summary>The raw referrer string Google Play recorded for this install, or null when
    /// there is none, the store is unavailable, or the platform has no such concept.
    ///
    /// <para>Non-throwing and time-bounded — this runs on a launch path and must never be
    /// able to hang it. Read <b>once</b> per install (Google's own guidance); the value is
    /// retained for 90 days and never changes short of a reinstall.</para>
    ///
    /// <para>The string is a URL-encoded query fragment, NOT a bare value. An organic Play
    /// install returns <c>utm_source=google-play&amp;utm_medium=organic</c>, which is why
    /// the parsed candidate is always validated against the real creator codes rather than
    /// trusted.</para></summary>
    Task<string?> GetInstallReferrerAsync();
}

/// <summary>Everywhere that is not Android. Nothing to read, so nothing is attributed and
/// the typed code is the only route in.</summary>
public sealed class NullInstallReferrerSource : IInstallReferrerSource
{
    public Task<string?> GetInstallReferrerAsync() => Task.FromResult<string?>(null);
}
