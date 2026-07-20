namespace Animal_Diary_App.Helpers;

/// <summary>
/// Cross-platform read of the OS "reduce motion" / "remove animations"
/// accessibility preference. MAUI has no unified API for this, so each
/// platform is queried directly. Used to freeze decorative background motion
/// (e.g. <c>WaterBackground</c>) for users who ask the system to calm things
/// down. Read once and cached — good enough for decorative animation gating.
/// </summary>
public static class ReducedMotion
{
    private static bool? _cached;

    public static bool IsEnabled => _cached ??= Query();

    private static bool Query()
    {
        try
        {
#if ANDROID
            // Animator duration scale of 0 == "Remove animations" in Accessibility.
            var resolver = Android.App.Application.Context?.ContentResolver;
            if (resolver is null)
                return false;
            float scale = Android.Provider.Settings.Global.GetFloat(
                resolver,
                Android.Provider.Settings.Global.AnimatorDurationScale,
                1f);
            return scale == 0f;
#elif IOS || MACCATALYST
            return UIKit.UIAccessibility.IsReduceMotionEnabled;
#elif WINDOWS
            return !new Windows.UI.ViewManagement.UISettings().AnimationsEnabled;
#else
            return false;
#endif
        }
        catch
        {
            // Never let an accessibility probe crash the UI — assume motion on.
            return false;
        }
    }
}
