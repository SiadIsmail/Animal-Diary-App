namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Controls.Shapes;
using Animal_Diary_App.Helpers;

/// <summary>
/// Reusable full-bleed page background: a vertical wash with soft,
/// input-transparent radial "glow" ellipses anchored near the corners (plus a
/// warm sand glow at the bottom edge), and a layer of hand-blown "bubbles" that
/// slowly drift and breathe. Blobs are sized as a fraction of the control's own
/// width so they scale with the device, and positioned by margin (not layout)
/// since they intentionally extend past the edges of the screen.
///
/// Honours the OS "reduce motion" setting: when enabled, every bubble is frozen
/// in place (no drift, no breathing) so the background stays perfectly still.
/// </summary>
public partial class WaterBackground : ContentView
{
    // Small sideways drift shared by every bubble; only the vertical range
    // and cycle length vary, so the field is per-bubble but this isn't.
    private const double BubbleXDrift = 8;

    // Peak scale at the breath's fullest — deliberately tiny so it reads as a
    // gentle swell rather than a pulse.
    private const double BubbleBreathScale = 1.05;

    private static readonly Random BubbleRandom = new();

    public WaterBackground()
    {
        InitializeComponent();
        SizeChanged += OnRootSizeChanged;

        // Accessibility: if the user asked the OS to reduce motion, leave the
        // bubbles exactly where they are and start nothing.
        if (ReducedMotion.IsEnabled)
            return;

        // Vertical range and duration vary per bubble so the drift never
        // reads as synchronized; each also gets a random startup delay
        // (a stand-in for CSS's negative animation-delay stagger, since
        // MAUI animations can't be scrubbed to an arbitrary phase). The
        // breathe cycle runs on its own, shorter, independent period so the
        // two motions stay out of phase — mirroring the HTML's separate
        // `drift` (13-28s) and `blob` (7-14s) keyframe timelines.
        StartBubble(Bubble1, yOffset: -16, driftMs: 13000, breatheMs: 13000);
        StartBubble(Bubble2, yOffset: 14, driftMs: 16000, breatheMs: 10000);
        StartBubble(Bubble3, yOffset: -10, driftMs: 19000, breatheMs: 9000);
        StartBubble(Bubble4, yOffset: 12, driftMs: 22000, breatheMs: 12000);
        StartBubble(Bubble5, yOffset: -14, driftMs: 25000, breatheMs: 8000);
        StartBubble(Bubble6, yOffset: 10, driftMs: 28000, breatheMs: 7000);
        StartBubble(Bubble7, yOffset: -8, driftMs: 15000, breatheMs: 14000);
        StartBubble(Bubble8, yOffset: 13, driftMs: 24000, breatheMs: 9000);
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0)
            return;

        PositionBlob(BlobTopLeft, Width * 1.4, centerXFraction: 0.08, centerYFraction: -0.06);
        PositionBlob(BlobTopRight, Width * 1.1, centerXFraction: 1.05, centerYFraction: -0.05);
        PositionBlob(BlobBottom, Width * 1.5, centerXFraction: 0.5, centerYFraction: 1.0);

        // Wide, shallow warm band: a large circle pushed mostly below the
        // bottom edge so only its top arc reads as a strip of warm sand.
        PositionBlob(BlobSand, Width * 1.8, centerXFraction: 0.5, centerYFraction: 1.18);
    }

    private void PositionBlob(Ellipse blob, double diameter, double centerXFraction, double centerYFraction)
    {
        blob.WidthRequest = diameter;
        blob.HeightRequest = diameter;

        double centerX = Width * centerXFraction;
        double centerY = Height * centerYFraction;

        blob.Margin = new Thickness(centerX - diameter / 2, centerY - diameter / 2, 0, 0);
    }

    private static void StartBubble(VisualElement bubble, double yOffset, uint driftMs, uint breatheMs)
    {
        uint driftDelay = (uint)BubbleRandom.Next(0, (int)driftMs);
        uint breatheDelay = (uint)BubbleRandom.Next(0, (int)breatheMs);
        _ = RunBubbleDriftAsync(bubble, yOffset, driftMs / 2, driftDelay);
        _ = RunBubbleBreatheAsync(bubble, breatheMs / 2, breatheDelay);
    }

    /// <summary>Infinite auto-reversing float: drifts to (X drift, yOffset) at
    /// the half-cycle mark, then eases back to origin, forever.</summary>
    private static async Task RunBubbleDriftAsync(VisualElement bubble, double yOffset, uint halfDurationMs, uint startDelayMs)
    {
        if (startDelayMs > 0)
            await Task.Delay((int)startDelayMs);

        while (true)
        {
            await bubble.TranslateTo(BubbleXDrift, yOffset, halfDurationMs, Easing.SinInOut);
            await bubble.TranslateTo(0, 0, halfDurationMs, Easing.SinInOut);
        }
    }

    /// <summary>Infinite auto-reversing breath: swells to <see cref="BubbleBreathScale"/>
    /// then eases back to 1.0 — the Ellipse analogue of the HTML's border-radius
    /// morph. Runs on its own period, independent of the drift.</summary>
    private static async Task RunBubbleBreatheAsync(VisualElement bubble, uint halfDurationMs, uint startDelayMs)
    {
        if (startDelayMs > 0)
            await Task.Delay((int)startDelayMs);

        while (true)
        {
            await bubble.ScaleTo(BubbleBreathScale, halfDurationMs, Easing.SinInOut);
            await bubble.ScaleTo(1.0, halfDurationMs, Easing.SinInOut);
        }
    }
}
