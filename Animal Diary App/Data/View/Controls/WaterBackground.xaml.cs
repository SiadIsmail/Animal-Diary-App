namespace Animal_Diary_App.Data.View.Controls;

using Microsoft.Maui.Controls.Shapes;
using Animal_Diary_App.Helpers;

/// <summary>
/// Reusable full-bleed page background: a vertical wash with soft,
/// input-transparent radial "glow" ellipses anchored near the corners (plus a
/// warm sand glow at the bottom edge), and a layer of hand-blown "bubbles" that
/// slowly drift. Blobs are sized as a fraction of the control's own width so they
/// scale with the device, and positioned by margin (not layout) since they
/// intentionally extend past the edges of the screen.
///
/// <para><b>The motion is owned by the hosting page, not by this control.</b> Call
/// <see cref="Start"/> from <c>OnAppearing</c> and <see cref="Stop"/> from
/// <c>OnDisappearing</c>. This is load-bearing, not tidiness: the three tab pages
/// are all alive at once for the Shell's lifetime, so a control that started its
/// own loops in the constructor left <b>three</b> copies drifting forever — including
/// on the two tabs nobody was looking at — and an Android Activity recreation added
/// three more that nothing could ever stop. Every one of those ticks lands on the UI
/// thread, competing with the tab switch the person is waiting for.</para>
///
/// <para>Honours the OS "reduce motion" accessibility preference: when enabled,
/// <see cref="Start"/> is a no-op and every bubble stays exactly where it is.</para>
/// </summary>
public partial class WaterBackground : ContentView
{
    // Small sideways drift shared by every bubble; only the vertical range
    // and cycle length vary, so the field is per-bubble but this isn't.
    private const double BubbleXDrift = 8;

    private static readonly Random BubbleRandom = new();

    /// <summary>Cancels the running drift loops. Null when nothing is animating,
    /// which is also the "already stopped" guard.</summary>
    private CancellationTokenSource? _motion;

    public WaterBackground()
    {
        InitializeComponent();
        SizeChanged += OnRootSizeChanged;
    }

    private VisualElement[] Bubbles =>
        new VisualElement[] { Bubble1, Bubble2, Bubble3, Bubble4, Bubble5, Bubble6, Bubble7, Bubble8 };

    /// <summary>Begin (or resume) the bubble drift. Idempotent — a second call while
    /// already running does nothing, so a page that appears twice can't double up.</summary>
    public void Start()
    {
        // Accessibility: if the user asked the OS to reduce motion, leave the
        // bubbles exactly where they are and start nothing.
        if (ReducedMotion.IsEnabled || _motion is not null)
            return;

        var cts = _motion = new CancellationTokenSource();
        var token = cts.Token;

        // Vertical range and duration vary per bubble so the drift never reads as
        // synchronized; each also gets a random startup delay (a stand-in for CSS's
        // negative animation-delay stagger, since MAUI animations can't be scrubbed
        // to an arbitrary phase).
        //
        // The prototype's second motion — a 5% "breathe" ScaleTo on its own shorter
        // period — is deliberately NOT here. It doubled the number of live animations
        // for a swell too small to see on a soft radial gradient, and scaling a
        // gradient-filled shape is the more expensive of the two transforms.
        StartBubble(Bubble1, yOffset: -16, driftMs: 13000, token);
        StartBubble(Bubble2, yOffset: 14, driftMs: 16000, token);
        StartBubble(Bubble3, yOffset: -10, driftMs: 19000, token);
        StartBubble(Bubble4, yOffset: 12, driftMs: 22000, token);
        StartBubble(Bubble5, yOffset: -14, driftMs: 25000, token);
        StartBubble(Bubble6, yOffset: 10, driftMs: 28000, token);
        StartBubble(Bubble7, yOffset: -8, driftMs: 15000, token);
        StartBubble(Bubble8, yOffset: 13, driftMs: 24000, token);
    }

    /// <summary>Stop every drift loop and release the frame callbacks. Idempotent.
    /// Bubbles are left wherever they are — freezing mid-drift is invisible on a
    /// decorative blob, and snapping them home would read as a flicker on tab exit.</summary>
    public void Stop()
    {
        var cts = Interlocked.Exchange(ref _motion, null);
        if (cts is null)
            return;

        cts.Cancel();
        cts.Dispose();

        // Cancel the in-flight tweens too. The loops check the token after each await,
        // but an animation left running would keep the ticker alive until it finished
        // its half-cycle — up to 14 seconds of frame callbacks after leaving the page.
        foreach (var bubble in Bubbles)
            bubble.CancelAnimations();
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

    private static void StartBubble(VisualElement bubble, double yOffset, uint driftMs, CancellationToken token)
    {
        uint driftDelay = (uint)BubbleRandom.Next(0, (int)driftMs);
        RunBubbleDriftAsync(bubble, yOffset, driftMs / 2, driftDelay, token).Forget();
    }

    /// <summary>Auto-reversing float: drifts to (X drift, yOffset) at the half-cycle
    /// mark, then eases back to origin, until the token is cancelled.</summary>
    private static async Task RunBubbleDriftAsync(
        VisualElement bubble, double yOffset, uint halfDurationMs, uint startDelayMs, CancellationToken token)
    {
        try
        {
            if (startDelayMs > 0)
                await Task.Delay((int)startDelayMs, token);

            // The token is checked after every leg, not just at the top: a leg is up to
            // 14 seconds long, and Stop() must not have to wait one out.
            while (!token.IsCancellationRequested)
            {
                await bubble.TranslateTo(BubbleXDrift, yOffset, halfDurationMs, Easing.SinInOut);
                if (token.IsCancellationRequested)
                    return;
                await bubble.TranslateTo(0, 0, halfDurationMs, Easing.SinInOut);
            }
        }
        catch (OperationCanceledException)
        {
            // Stop() during the startup delay. Nothing to unwind.
        }
    }
}
