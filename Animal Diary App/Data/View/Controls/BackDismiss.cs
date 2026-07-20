namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Data.View;

/// <summary>
/// Android back-button handling for the app's overlays.
///
/// A page hosts its sheets as overlay <c>ContentView</c>s that stay in the visual
/// tree even while closed (a hidden <see cref="FelovaBottomSheet"/> is translated
/// off-screen, never collapsed — see the sheet's own remarks). So "is something
/// open?" can be answered by walking the page once and asking each overlay,
/// instead of every page hand-listing its sheet ViewModels.
///
/// Pages opt in with a single override:
/// <code>
/// protected override bool OnBackButtonPressed()
///     =&gt; BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();
/// </code>
/// When nothing is open the call is a no-op and normal navigation is untouched.
/// </summary>
public static class BackDismiss
{
    /// <summary>Closes the top-most open overlay inside <paramref name="root"/> and
    /// reports whether it closed anything. "Top-most" is the last one in visual-tree
    /// order, which is also what renders on top — so the settings panel, declared
    /// after the sheets, wins while it is open.</summary>
    public static bool TryCloseTopmostOverlay(Element root)
    {
        Action? close = null;

        foreach (var element in root.GetVisualTreeDescendants())
        {
            switch (element)
            {
                // Dismiss through the sheet's own command when it has one, exactly as
                // a scrim tap does — the VM may need to reset draft state, not just
                // slide the sheet away.
                case FelovaBottomSheet { IsPresented: true } sheet:
                    close = () =>
                    {
                        if (sheet.DismissCommand is { } cmd && cmd.CanExecute(null))
                            cmd.Execute(null);
                        else
                            sheet.IsPresented = false;
                    };
                    break;

                case SettingsPanelView { IsPanelOpen: true } panel:
                    close = () => panel.IsPanelOpen = false;
                    break;
            }
        }

        close?.Invoke();
        return close is not null;
    }
}
