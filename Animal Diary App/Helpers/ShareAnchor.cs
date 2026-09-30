namespace Animal_Diary_App.Helpers;

/// <summary>
/// The rectangle iPadOS anchors a share or "open with" popover to.
///
/// <para><b>Why this exists.</b> On iPad, <c>Share.RequestAsync</c> and
/// <c>Launcher.OpenAsync(OpenFileRequest)</c> present a
/// <c>UIPopoverPresentationController</c>, and a popover with no source rectangle
/// THROWS rather than degrading: the OS has nowhere to point the arrow. iPhone
/// presents the same request as a bottom sheet and ignores the rectangle entirely,
/// and every other platform ignores it too, so supplying one is free everywhere and
/// load-bearing in exactly one place.</para>
///
/// <para><b>Why a screen rectangle and not the button's.</b> The correct anchor is the
/// bounds of the control that was tapped, but both callers (<c>ReportActions</c> and
/// <c>ConstellationShare</c>) are static helpers reached from several surfaces, and
/// threading a <c>View</c> through all of them to serve a device the app does not
/// currently ship on (v1 is iPhone-only, see docs/history/IOS_PLAN.md §5.1) would cost
/// more than it returns. Every one of those surfaces invokes the share from a control
/// low on the screen, inside a bottom sheet, so a small rectangle centred horizontally
/// and low vertically puts the arrow near enough to where the finger was.</para>
///
/// <para>Revisit this the day iPad becomes a supported family: at that point the
/// anchor should come from the actual button, and this helper becomes the fallback for
/// callers that genuinely have no view.</para>
/// </summary>
public static class ShareAnchor
{
    /// <summary>Anchor rectangle in device-independent units, in screen coordinates.</summary>
    public static Rect Bounds()
    {
        try
        {
            var display = DeviceDisplay.Current.MainDisplayInfo;

            // MainDisplayInfo is in physical pixels; the popover wants the same units
            // MAUI lays out in, so divide by density. Density can be reported as 0 on a
            // display that has not been queried yet, which would make this infinite.
            var density = display.Density > 0 ? display.Density : 1;
            var width = display.Width / density;
            var height = display.Height / density;

            // A 1pt target rather than a 0-sized one: a zero-area source rect is treated
            // as "unset" by some iOS versions, which puts the throw back.
            return new Rect(width / 2, height * 0.75, 1, 1);
        }
        catch
        {
            // Never let anchor maths stop a share. A rectangle at the origin still
            // satisfies the popover; it simply points at the top-left corner.
            return new Rect(0, 0, 1, 1);
        }
    }
}
