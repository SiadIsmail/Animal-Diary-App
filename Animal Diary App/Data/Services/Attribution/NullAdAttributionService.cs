namespace Animal_Diary_App.Data.Services.Attribution;

/// <summary>
/// The no-op. Registered on every platform without a Meta SDK (iOS, MacCatalyst, Windows)
/// and on any build where <c>MetaAdsConfig.Enabled</c> is false or the credentials are
/// missing. Reports nothing, initializes nothing, and hides the Settings toggle by way of
/// <see cref="IsAvailable"/>.
///
/// <para>iOS lands here on purpose for now. Meta's iOS SDK is a separate binding job and
/// the app does not ship on the App Store yet; when it does, an iOS implementation slots in
/// beside the Android one and nothing above this interface changes. See
/// AI/current-roadmap.md.</para>
/// </summary>
public sealed class NullAdAttributionService : IAdAttributionService
{
    public bool IsAvailable => false;

    public bool Enabled => false;

    public void Start() { }

    public void SetEnabled(bool enabled) { }
}
