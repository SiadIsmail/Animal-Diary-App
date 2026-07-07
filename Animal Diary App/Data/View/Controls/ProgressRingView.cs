namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Helpers;
using Microsoft.Maui.Graphics;

/// <summary>
/// A precise circular progress ring — a clean full-circle track with a
/// foreground arc sweeping clockwise from 12 o'clock for <see cref="Progress"/>
/// (0..1). This is a DATA readout (care completion), so its geometry is exact:
/// no wobble, no sketch effect. Changes to <see cref="Progress"/> animate the
/// arc fill with standard easing (or snap, under reduced-motion).
/// </summary>
public sealed class ProgressRingView : ContentView
{
    private readonly GraphicsView _surface;
    private readonly RingDrawable _drawable = new();

    public ProgressRingView()
    {
        _surface = new GraphicsView { Drawable = _drawable };
        Content = _surface;
    }

    public static readonly BindableProperty ProgressProperty = BindableProperty.Create(
        nameof(Progress), typeof(double), typeof(ProgressRingView), 0d,
        propertyChanged: OnProgressChanged);

    /// <summary>Fill fraction, 0..1.</summary>
    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    public static readonly BindableProperty ProgressColorProperty = BindableProperty.Create(
        nameof(ProgressColor), typeof(Color), typeof(ProgressRingView), Colors.Teal,
        propertyChanged: (b, o, n) => ((ProgressRingView)b).Redraw());

    public Color ProgressColor
    {
        get => (Color)GetValue(ProgressColorProperty);
        set => SetValue(ProgressColorProperty, value);
    }

    public static readonly BindableProperty TrackColorProperty = BindableProperty.Create(
        nameof(TrackColor), typeof(Color), typeof(ProgressRingView), Colors.LightGray,
        propertyChanged: (b, o, n) => ((ProgressRingView)b).Redraw());

    public Color TrackColor
    {
        get => (Color)GetValue(TrackColorProperty);
        set => SetValue(TrackColorProperty, value);
    }

    public static readonly BindableProperty RingThicknessProperty = BindableProperty.Create(
        nameof(RingThickness), typeof(double), typeof(ProgressRingView), 5d,
        propertyChanged: (b, o, n) => ((ProgressRingView)b).Redraw());

    public double RingThickness
    {
        get => (double)GetValue(RingThicknessProperty);
        set => SetValue(RingThicknessProperty, value);
    }

    private void Redraw()
    {
        _drawable.ProgressColor = ProgressColor;
        _drawable.TrackColor = TrackColor;
        _drawable.Thickness = (float)RingThickness;
        _surface.Invalidate();
    }

    private static void OnProgressChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (ProgressRingView)bindable;
        double from = view._drawable.Progress;
        double to = Math.Clamp((double)newValue, 0, 1);

        view.Redraw();

        // Reduced motion (or first paint from 0): snap, don't sweep.
        if (ReducedMotion.IsEnabled)
        {
            view._drawable.Progress = to;
            view._surface.Invalidate();
            return;
        }

        view.AbortAnimation("ring");
        new Animation(v =>
        {
            view._drawable.Progress = v;
            view._surface.Invalidate();
        }, from, to)
        .Commit(view, "ring", length: 850, easing: Easing.CubicInOut);
    }

    private sealed class RingDrawable : IDrawable
    {
        public double Progress { get; set; }
        public Color ProgressColor { get; set; } = Colors.Teal;
        public Color TrackColor { get; set; } = Colors.LightGray;
        public float Thickness { get; set; } = 5f;

        public void Draw(ICanvas canvas, RectF rect)
        {
            float inset = Thickness / 2f + 1f;
            var box = new RectF(rect.X + inset, rect.Y + inset,
                                rect.Width - inset * 2, rect.Height - inset * 2);

            canvas.StrokeSize = Thickness;
            canvas.StrokeLineCap = LineCap.Round;

            // Full track.
            canvas.StrokeColor = TrackColor;
            canvas.DrawEllipse(box);

            if (Progress <= 0)
                return;

            // Foreground arc: clockwise from 12 o'clock (90°) by Progress·360.
            float sweep = (float)(360 * Math.Clamp(Progress, 0, 1));
            canvas.StrokeColor = ProgressColor;
            // Start at 12 o'clock (90°), sweep clockwise by `sweep` degrees.
            canvas.DrawArc(box, 90f, 90f - sweep, true, false);
        }
    }
}
