namespace Animal_Diary_App.Data.Services.Attribution;

/// <summary>
/// The owner's per-device choice about Meta install attribution, stored in
/// <see cref="Preferences"/> (device-local, like the daily-reminder settings and the pause
/// state — never synced, never sent anywhere).
///
/// <para><b>On by default, off by one switch in Settings.</b> That is a deliberate product
/// decision, not an oversight: the app measures installs so paid acquisition can be
/// evaluated at all, and the owner can end it at any time. The consequence to be honest
/// about is that an EU/DE install starts measuring before anyone has been asked, which is
/// a weaker consent posture than an opt-in would be. If that ever needs to change, the
/// switch is <see cref="DefaultEnabled"/> plus a first-run prompt — nothing else in this
/// subsystem has to move.</para>
///
/// <para><b>A data reset deliberately does NOT clear this.</b> Every other Preferences-backed
/// value is wiped by <c>AppResetService</c> so the device looks freshly installed, but a
/// privacy choice is not app state — silently switching tracking back on for someone who
/// turned it off would be the one outcome a reset must never produce. It is the same reason
/// the analytics id is rotated rather than the analytics switch being flipped.</para>
/// </summary>
public static class AdTrackingSettings
{
    private const string EnabledKey = "meta_ad_tracking_enabled";

    /// <summary>What a device that has never touched the switch reports. See the class
    /// summary before changing this — it is the whole consent posture in one bool.</summary>
    private const bool DefaultEnabled = true;

    /// <summary>Whether this device may report its install to Meta.</summary>
    public static bool Enabled
    {
        get => Preferences.Default.Get(EnabledKey, DefaultEnabled);
        set => Preferences.Default.Set(EnabledKey, value);
    }

    /// <summary>True once the owner has actually moved the switch, either way. Nothing
    /// reads this today; it exists so a later consent surface can tell "chose to leave it
    /// on" apart from "was never asked", which the bool alone cannot express.</summary>
    public static bool HasChosen => Preferences.Default.ContainsKey(EnabledKey);
}
