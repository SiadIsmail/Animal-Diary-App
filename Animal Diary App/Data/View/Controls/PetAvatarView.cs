namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Graphics;

/// <summary>
/// The pet's circular avatar, shared by every surface that shows a pet (Today,
/// Pets, Manage, the details/preview page). It renders the profile photo when one
/// is set AND its file exists on this device, otherwise a fallback glyph — the type
/// emoji, or a name initial. One control keeps all avatar sites visually identical
/// and gives photos a single place to appear.
///
/// A <see cref="PhotoPath"/> that names a file this device doesn't have (e.g. a row
/// synced from another device, which carries the file name but not the image) falls
/// back to the glyph automatically — the File.Exists guard handles it.
/// </summary>
public sealed class PetAvatarView : ContentView
{
    private readonly Border _frame;
    private readonly Image _photo;
    private readonly Label _fallback;

    public PetAvatarView()
    {
        _photo = new Image { Aspect = Aspect.AspectFill };
        _fallback = new Label
        {
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };

        // Fallback sits under the photo; when a photo is shown it covers the glyph.
        _frame = new Border
        {
            StrokeThickness = 1,
            Content = new Grid { Children = { _fallback, _photo } },
        };

        // Background gradient + stroke come from the shared palette (resolved from
        // app resources — the accepted convention for presentation tokens) so the
        // avatar matches the app's other rounded surfaces.
        if (TryColor("SeaHi", out var hi) && TryColor("SeaMid", out var mid))
            _frame.Background = new LinearGradientBrush(
                new GradientStopCollection { new(hi, 0f), new(mid, 1f) },
                new Point(0, 0), new Point(1, 1));
        if (TryColor("GlassLine", out var line))
            _frame.Stroke = line;

        Content = _frame;
        ApplyDiameter();
        ApplyFallback();
        ApplyPhoto();
    }

    // ── PhotoPath ────────────────────────────────────────────────────────────────
    public static readonly BindableProperty PhotoPathProperty = BindableProperty.Create(
        nameof(PhotoPath), typeof(string), typeof(PetAvatarView), null,
        propertyChanged: (b, _, _) => ((PetAvatarView)b).ApplyPhoto());

    /// <summary>Absolute path to the pet's photo, or null/empty for none. Bind to
    /// <c>Pet.PhotoFullPath</c>.</summary>
    public string? PhotoPath
    {
        get => (string?)GetValue(PhotoPathProperty);
        set => SetValue(PhotoPathProperty, value);
    }

    // ── FallbackText ─────────────────────────────────────────────────────────────
    public static readonly BindableProperty FallbackTextProperty = BindableProperty.Create(
        nameof(FallbackText), typeof(string), typeof(PetAvatarView), "🐾",
        propertyChanged: (b, _, _) => ((PetAvatarView)b).ApplyFallback());

    /// <summary>Glyph shown when there is no photo — the pet's type emoji or a name
    /// initial, chosen by the host.</summary>
    public string FallbackText
    {
        get => (string)GetValue(FallbackTextProperty);
        set => SetValue(FallbackTextProperty, value);
    }

    // ── Diameter ─────────────────────────────────────────────────────────────────
    public static readonly BindableProperty DiameterProperty = BindableProperty.Create(
        nameof(Diameter), typeof(double), typeof(PetAvatarView), 60d,
        propertyChanged: (b, _, _) => ((PetAvatarView)b).ApplyDiameter());

    /// <summary>Width/height of the circle, in device-independent units. The corner
    /// radius is half of this, so the avatar is always a perfect circle.</summary>
    public double Diameter
    {
        get => (double)GetValue(DiameterProperty);
        set => SetValue(DiameterProperty, value);
    }

    // ── FallbackFontSize ─────────────────────────────────────────────────────────
    public static readonly BindableProperty FallbackFontSizeProperty = BindableProperty.Create(
        nameof(FallbackFontSize), typeof(double), typeof(PetAvatarView), 30d,
        propertyChanged: (b, _, _) => ((PetAvatarView)b).ApplyFallback());

    /// <summary>Font size of the fallback glyph.</summary>
    public double FallbackFontSize
    {
        get => (double)GetValue(FallbackFontSizeProperty);
        set => SetValue(FallbackFontSizeProperty, value);
    }

    private void ApplyDiameter()
    {
        var d = Diameter;
        _frame.WidthRequest = d;
        _frame.HeightRequest = d;
        _frame.StrokeShape = new RoundRectangle { CornerRadius = d / 2 };
    }

    private void ApplyFallback()
    {
        _fallback.Text = FallbackText;
        _fallback.FontSize = FallbackFontSize;
    }

    private void ApplyPhoto()
    {
        var path = PhotoPath;
        var hasPhoto = !string.IsNullOrEmpty(path) && File.Exists(path);

        _photo.Source = hasPhoto ? ImageSource.FromFile(path) : null;
        _photo.IsVisible = hasPhoto;
        _fallback.IsVisible = !hasPhoto;
    }

    private static bool TryColor(string key, out Color color)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Color c)
        {
            color = c;
            return true;
        }
        color = Colors.Transparent;
        return false;
    }
}
