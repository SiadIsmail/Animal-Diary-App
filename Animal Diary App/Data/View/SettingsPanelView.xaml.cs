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
        if (open)
        {
            RootOverlay.IsVisible = true;
            SettingsPanel.TranslationX = 300;
            DimmingOverlay.Opacity = 0;

            await Task.WhenAll(
                SettingsPanel.TranslateTo(0, 0, 260, Easing.CubicOut),
                DimmingOverlay.FadeTo(0.5, 200));
        }
        else
        {
            await Task.WhenAll(
                SettingsPanel.TranslateTo(300, 0, 210, Easing.CubicIn),
                DimmingOverlay.FadeTo(0, 180));

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
