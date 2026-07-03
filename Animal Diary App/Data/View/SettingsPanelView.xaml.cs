namespace Animal_Diary_App.Data.View;

public partial class SettingsPanelView : ContentView
{
    public static readonly BindableProperty IsPanelOpenProperty = BindableProperty.Create(
        nameof(IsPanelOpen),
        typeof(bool),
        typeof(SettingsPanelView),
        false,
        BindingMode.TwoWay,
        propertyChanged: OnIsPanelOpenChanged);

    public bool IsPanelOpen
    {
        get => (bool)GetValue(IsPanelOpenProperty);
        set => SetValue(IsPanelOpenProperty, value);
    }

    public SettingsPanelView()
    {
        InitializeComponent();
    }

    private static void OnIsPanelOpenChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((SettingsPanelView)bindable).AnimatePanel((bool)newValue);
    }

    private async void AnimatePanel(bool open)
    {
        // Off-screen offset. Fall back to the requested width when the panel
        // has not been measured yet (typical on the first open on Android).
        double offset = (SettingsPanel.Width > 0 ? SettingsPanel.Width : SettingsPanel.WidthRequest) + 20;

        if (open)
        {
            RootOverlay.IsVisible = true;
            SettingsPanel.TranslationX = 0;
            DimmingOverlay.Opacity = 0;

            // Give Android one layout/render pass before animating the panel
            // that was just made visible; otherwise TranslateTo is skipped and
            // the panel never slides in (only the dimming overlay shows).
            await Task.Delay(16);

            await Task.WhenAll(
                SettingsPanel.TranslateToAsync(0, 0, 260, Easing.CubicOut),
                DimmingOverlay.FadeToAsync(0.5, 200));
        }
        else
        {
            await Task.WhenAll(
                SettingsPanel.TranslateToAsync(offset, 0, 210, Easing.CubicIn),
                DimmingOverlay.FadeToAsync(0, 180));

            RootOverlay.IsVisible = false;
        }
    }

    private void OnOverlayTapped(object? sender, TappedEventArgs e)
    {
        IsPanelOpen = false;
    }

    private void OnCloseTapped(object? sender, TappedEventArgs e)
    {
        IsPanelOpen = false;
    }
}
