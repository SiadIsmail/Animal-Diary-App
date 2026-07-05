namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Controls.Shapes;

/// <summary>
/// Reusable full-bleed page background: a vertical wash with three soft,
/// input-transparent radial "glow" ellipses anchored near the corners. Sized
/// as a fraction of the control's own width so the blobs scale with the
/// device, positioned by margin (not layout) since they intentionally
/// extend past the edges of the screen.
/// </summary>
public partial class WaterBackground : ContentView
{
    // Small sideways drift shared by every bubble; only the vertical range
    // and cycle length vary, so the field is per-bubble but this isn't.
    private const double BubbleXDrift = 8;

    private static readonly Random BubbleRandom = new();

    public WaterBackground()
    {
        InitializeComponent();
        SizeChanged += OnRootSizeChanged;

        // Vertical range and duration vary per bubble so the drift never
        // reads as synchronized; each also gets a random startup delay
        // (a stand-in for CSS's negative animation-delay stagger, since
        // MAUI animations can't be scrubbed to an arbitrary phase).
        StartBubble(Bubble1, yOffset: -16, durationMs: 13000);
        StartBubble(Bubble2, yOffset: 14, durationMs: 16000);
        StartBubble(Bubble3, yOffset: -10, durationMs: 19000);
        StartBubble(Bubble4, yOffset: 12, durationMs: 22000);
        StartBubble(Bubble5, yOffset: -14, durationMs: 25000);
        StartBubble(Bubble6, yOffset: 10, durationMs: 28000);
        StartBubble(Bubble7, yOffset: -8, durationMs: 15000);
        StartBubble(Bubble8, yOffset: 13, durationMs: 24000);
    }

    private void OnRootSizeChanged(object? sender, EventArgs e)
    {
        if (Width <= 0 || Height <= 0)
            return;

        PositionBlob(BlobTopLeft, Width * 1.4, centerXFraction: 0.08, centerYFraction: -0.06);
        PositionBlob(BlobTopRight, Width * 1.1, centerXFraction: 1.05, centerYFraction: -0.05);
        PositionBlob(BlobBottom, Width * 1.5, centerXFraction: 0.5, centerYFraction: 1.0);
    }

    private void PositionBlob(Ellipse blob, double diameter, double centerXFraction, double centerYFraction)
    {
        blob.WidthRequest = diameter;
        blob.HeightRequest = diameter;

        double centerX = Width * centerXFraction;
        double centerY = Height * centerYFraction;

        blob.Margin = new Thickness(centerX - diameter / 2, centerY - diameter / 2, 0, 0);
    }

    private static void StartBubble(VisualElement bubble, double yOffset, uint durationMs)
    {
        uint startDelay = (uint)BubbleRandom.Next(0, (int)durationMs);
        _ = RunBubbleLoopAsync(bubble, yOffset, durationMs / 2, startDelay);
    }

    /// <summary>Infinite auto-reversing float: drifts to (X drift, yOffset) at
    /// the half-cycle mark, then eases back to origin, forever.</summary>
    private static async Task RunBubbleLoopAsync(VisualElement bubble, double yOffset, uint halfDurationMs, uint startDelayMs)
    {
        if (startDelayMs > 0)
            await Task.Delay((int)startDelayMs);

        while (true)
        {
            await bubble.TranslateTo(BubbleXDrift, yOffset, halfDurationMs, Easing.SinInOut);
            await bubble.TranslateTo(0, 0, halfDurationMs, Easing.SinInOut);
        }
    }
}
