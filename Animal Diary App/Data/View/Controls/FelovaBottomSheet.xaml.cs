namespace Animal_Diary_App.Data.View.Controls;

using System.Windows.Input;
using Animal_Diary_App.Helpers;

// This namespace contains a "View" segment, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

/// <summary>
/// The single reusable slide-up bottom sheet (see the XAML header). Owns the
/// scrim + slide/fade motion and hosts arbitrary <see cref="SheetContent"/>.
/// Used by both the medication add/edit form and the Journal input sheets so the
/// person never feels they "left" the page to log something.
/// </summary>
public partial class FelovaBottomSheet : ContentView
{
    // Prototype feel: ~420ms slide on cubic-bezier(.32,.72,.28,1), scrim fade.
    private const uint SlideInMs = 420;
    private const uint SlideOutMs = 360;
    private const uint ScrimInMs = 300;
    private const uint ScrimOutMs = 220;
    // Reduced motion: keep it near-instant but not jarring.
    private const uint ReducedMs = 90;

    /// <summary>The prototype's easing curve, cubic-bezier(.32,.72,.28,1), shared so
    /// every sheet slides with the same hand.</summary>
    public static readonly Easing SheetEasing = new(BezierEase);

    private bool isAnimating;

    public FelovaBottomSheet()
    {
        InitializeComponent();
    }

    // ── Bindable surface ───────────────────────────────────────────────────────

    public static readonly BindableProperty IsPresentedProperty = BindableProperty.Create(
        nameof(IsPresented), typeof(bool), typeof(FelovaBottomSheet), false,
        BindingMode.TwoWay, propertyChanged: OnIsPresentedChanged);

    /// <summary>Flip to slide the sheet in (true) or out (false).</summary>
    public bool IsPresented
    {
        get => (bool)GetValue(IsPresentedProperty);
        set => SetValue(IsPresentedProperty, value);
    }

    public static readonly BindableProperty TitleProperty = BindableProperty.Create(
        nameof(Title), typeof(string), typeof(FelovaBottomSheet), string.Empty);

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public static readonly BindableProperty SubtitleProperty = BindableProperty.Create(
        nameof(Subtitle), typeof(string), typeof(FelovaBottomSheet), string.Empty);

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }

    public static readonly BindableProperty SheetContentProperty = BindableProperty.Create(
        nameof(SheetContent), typeof(View), typeof(FelovaBottomSheet), null,
        propertyChanged: OnSheetContentChanged);

    /// <summary>The arbitrary body hosted inside the sheet.</summary>
    public View? SheetContent
    {
        get => (View?)GetValue(SheetContentProperty);
        set => SetValue(SheetContentProperty, value);
    }

    public static readonly BindableProperty DismissCommandProperty = BindableProperty.Create(
        nameof(DismissCommand), typeof(ICommand), typeof(FelovaBottomSheet), null);

    /// <summary>Invoked when the scrim is tapped. When null, tapping the scrim just
    /// sets <see cref="IsPresented"/> to false.</summary>
    public ICommand? DismissCommand
    {
        get => (ICommand?)GetValue(DismissCommandProperty);
        set => SetValue(DismissCommandProperty, value);
    }

    private static void OnSheetContentChanged(BindableObject bindable, object oldValue, object newValue)
    {
        ((FelovaBottomSheet)bindable).BodyHost.Content = newValue as View;
    }

    private static async void OnIsPresentedChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var sheet = (FelovaBottomSheet)bindable;
        if ((bool)newValue)
            await sheet.ShowAsync();
        else
            await sheet.HideAsync();
    }

    // ── Layout + motion ────────────────────────────────────────────────────────

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        // Cap the sheet to most of the screen so a tall body scrolls its content
        // instead of pushing actions off-screen.
        if (height > 0)
            SheetContainer.MaximumHeightRequest = height * 0.9;
    }

    private async Task ShowAsync()
    {
        if (isAnimating)
            return;
        isAnimating = true;

        var reduce = ReducedMotion.IsEnabled;
        var offset = SheetContainer.Height > 0 ? SheetContainer.Height : 600;

        // Start fully below and transparent, then slide up + fade the scrim in.
        SheetContainer.TranslationY = offset;
        Scrim.Opacity = 0;
        IsVisible = true;

        // Android: the body was collapsed (IsVisible=false) while hidden, so content
        // populated just before presenting — especially a BindableLayout whose items
        // were assigned on-open — measures short (or empty) on this first show and only
        // corrects on a later, unrelated relayout. Force the body to remeasure now that
        // it's on-screen, and again on the next tick once the platform has settled.
        InvalidateBody();
        Dispatcher.Dispatch(InvalidateBody);

        await Task.WhenAll(
            Scrim.FadeTo(1, reduce ? ReducedMs : ScrimInMs, Easing.CubicOut),
            SheetContainer.TranslateTo(0, 0, reduce ? ReducedMs : SlideInMs, SheetEasing));

        InvalidateBody();
        isAnimating = false;
    }

    // Re-measure the sheet + its hosted body (see ShowAsync). A no-op cost when nothing
    // has changed; on Android it's what makes late-populated content lay out on first show.
    private void InvalidateBody()
    {
        (SheetContainer as Microsoft.Maui.IView)?.InvalidateMeasure();
        (BodyHost?.Content as Microsoft.Maui.IView)?.InvalidateMeasure();
    }

    private async Task HideAsync()
    {
        if (isAnimating)
            return;
        isAnimating = true;

        var reduce = ReducedMotion.IsEnabled;
        var offset = SheetContainer.Height > 0 ? SheetContainer.Height : 600;

        await Task.WhenAll(
            Scrim.FadeTo(0, reduce ? ReducedMs : ScrimOutMs, Easing.CubicIn),
            SheetContainer.TranslateTo(0, offset, reduce ? ReducedMs : SlideOutMs, SheetEasing));

        IsVisible = false;
        SheetContainer.TranslationY = 0;

        isAnimating = false;
    }

    private void OnScrimTapped(object? sender, TappedEventArgs e)
    {
        if (DismissCommand is { } cmd && cmd.CanExecute(null))
            cmd.Execute(null);
        else
            IsPresented = false;
    }

    // cubic-bezier(.32,.72,.28,1): solve x(u)=t for the curve parameter u by a few
    // Newton steps, then return y(u). Gives the prototype's exact slide feel.
    private static double BezierEase(double t)
    {
        const double p1x = 0.32, p1y = 0.72, p2x = 0.28, p2y = 1.0;

        double u = t;
        for (int i = 0; i < 5; i++)
        {
            double x = Component(u, p1x, p2x) - t;
            double dx = Derivative(u, p1x, p2x);
            if (Math.Abs(dx) < 1e-6)
                break;
            u = Math.Clamp(u - x / dx, 0, 1);
        }
        return Component(u, p1y, p2y);

        // Cubic Bézier component with P0=0, P3=1 and the two given control values.
        static double Component(double u, double a, double b)
        {
            double m = 1 - u;
            return 3 * m * m * u * a + 3 * m * u * u * b + u * u * u;
        }

        static double Derivative(double u, double a, double b)
        {
            double m = 1 - u;
            return 3 * m * m * a + 6 * m * u * (b - a) + 3 * u * u * (1 - b);
        }
    }
}
