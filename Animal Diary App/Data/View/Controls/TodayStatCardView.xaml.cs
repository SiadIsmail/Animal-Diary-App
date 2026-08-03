namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Controls.Shapes;

/// <summary>
/// One Today stat card. Purely presentational — its <c>BindingContext</c> is a
/// <c>TodayCardItem</c>, which owns both the reading and the tap that changes it.
/// </summary>
public partial class TodayStatCardView : ContentView
{
    public TodayStatCardView() => InitializeComponent();

    /// <summary>Mirrors the card's hand-drawn corner radii so the pair beside each other
    /// reads as two torn-out notes rather than two identical boxes — the same
    /// "imperfection on the frame, never on the readout" rule the rest of Today follows.
    /// A bool rather than a bindable <c>IShape</c>: there are exactly two cards, and the
    /// only question is which of them is the left one.</summary>
    public static readonly BindableProperty MirroredProperty = BindableProperty.Create(
        nameof(Mirrored), typeof(bool), typeof(TodayStatCardView), false,
        propertyChanged: OnMirroredChanged);

    public bool Mirrored
    {
        get => (bool)GetValue(MirroredProperty);
        set => SetValue(MirroredProperty, value);
    }

    private static void OnMirroredChanged(BindableObject bindable, object oldValue, object newValue)
    {
        if (bindable is not TodayStatCardView view)
            return;

        view.CardBorder.StrokeShape = new RoundRectangle
        {
            CornerRadius = (bool)newValue
                ? new CornerRadius(13, 16, 15, 14)
                : new CornerRadius(16, 13, 14, 15)
        };
    }
}
