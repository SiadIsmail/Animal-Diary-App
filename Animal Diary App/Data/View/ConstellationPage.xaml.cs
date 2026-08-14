namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using System.Diagnostics;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.View.Controls;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;

/// <summary>
/// The Constellation's view half: it owns the canvas, the two numbers that describe
/// how the sky is being looked at (zoom and scroll), and the arithmetic that turns a
/// fingertip into a star. The ViewModel owns the range, the events and the selection.
///
/// <para>The split is the same one the photo cropper draws: only the page knows how
/// big the canvas is, so only the page can place anything on it — and keeping
/// placement out of the ViewModel is what lets <c>ConstellationLayout</c> stay pure
/// and testable.</para>
/// </summary>
public partial class ConstellationPage : ContentPage
{
    private readonly MainViewModel vm;
    private readonly ConstellationDrawable _drawable = new();

    /// <summary>1 = the whole chosen stretch fits the canvas. A camera over a fixed
    /// layout, not a re-layout: see the note on <c>ConstellationDrawable</c>'s camera
    /// for why the difference is the whole feature.</summary>
    private double _zoom = 1;
    private double _scrollX;

    /// <summary>Where the sky sat when the current drag began. A pan reports its total
    /// travel since the finger went down, so the offset is always start + total — never
    /// an accumulation, which drifts over a long drag.</summary>
    private double _panStartScroll;

    private bool _tracked;
    private bool _sharing;

    /// <summary>How near a tap has to land, in canvas units. Generous on purpose: the
    /// symbols are a few pixels across in a busy sky, and the nearest one wins, so a
    /// wide reach costs nothing and a narrow one makes dense patches untappable.</summary>
    private const double TapReach = 22.0;

    private const double MinZoom = 1.0;

    public ConstellationPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;

        Sky.Drawable = _drawable;

        // The canvas has no size until it is laid out, and it changes on rotation.
        SkyHost.SizeChanged += (_, _) => Rebuild();
    }

    // Android back closes the tapped-star sheet before it navigates.
    protected override bool OnBackButtonPressed()
        => BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.ConstellationVM.SkyChanged += OnSkyChanged;
        vm.ConstellationVM.PropertyChanged += OnViewModelPropertyChanged;
        // Another caregiver's entries landing while this is open belong in the sky.
        vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;

        try
        {
            await vm.ConstellationVM.LoadAsync();

            // Once per visit, not once per range change: this measures whether the
            // surface is found at all, and re-firing per tab would drown that.
            if (!_tracked)
            {
                _tracked = true;
                vm.ConstellationVM.TrackOpened();
            }
        }
        catch (Exception ex)
        {
            // A failed load degrades to an empty sky — never crash the app (async void).
            Debug.WriteLine($"[Constellation] load failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vm.ConstellationVM.SkyChanged -= OnSkyChanged;
        vm.ConstellationVM.PropertyChanged -= OnViewModelPropertyChanged;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
    }

    private void OnRemoteChangesApplied() =>
        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                await vm.ConstellationVM.LoadAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Constellation] cloud reload failed: {ex}");
            }
        });

    /// <summary>A new set of events (a new range, a new pet, a sync). The view of the
    /// sky resets with it: a scroll position measured against last month's stretch
    /// means nothing against this one.</summary>
    private void OnSkyChanged()
    {
        _zoom = MinZoom;
        _scrollX = 0;
        Rebuild();
        FitToData();
    }

    /// <summary>
    /// Open on where the entries actually are.
    ///
    /// <para>Sixty entries over ninety days, the first six weeks of it empty, gave a
    /// card that was half dead space — honest, and a poor thing to look at or to
    /// share. This moves the CAMERA rather than the range: nothing is hidden, the
    /// chosen stretch is still the chosen stretch, and pinching out reaches the empty
    /// weeks like any other part of the sky.</para>
    /// </summary>
    private void FitToData()
    {
        var width = SkyHost.Width;
        var stars = _drawable.Stars;
        if (width <= 0 || stars.Count < 2)
            return;

        double first = double.MaxValue, last = double.MinValue;
        foreach (var star in stars)
        {
            first = Math.Min(first, star.X);
            last = Math.Max(last, star.X);
        }

        var span = last - first;
        if (span <= 0)
            return;

        // A margin either side, so the outermost entries are inside the frame rather
        // than clipped to it.
        var padded = span * 1.12;
        if (padded >= width)
            return;

        _zoom = Math.Clamp(width / padded, MinZoom, MaxZoom());
        _scrollX = (first + last) / 2 * _zoom - width / 2;
        ApplyCamera();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConstellationViewModel.SelectedIndex))
            return;

        _drawable.SelectedIndex = vm.ConstellationVM.SelectedIndex;
        Sky.Invalidate();
    }

    // ── Placement ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Place every star, once, in world units — the canvas's own width at zoom 1.
    /// Runs on layout and on a new range, and <b>never on a zoom step</b>: the whole
    /// point of the camera is that the constellation keeps its shape while it is
    /// magnified. Re-placing here would put the stars back where the old version had
    /// them, jumping between ring positions on the way in.
    /// </summary>
    private void Rebuild()
    {
        var width = SkyHost.Width;
        var height = SkyHost.Height;
        if (width <= 0 || height <= 0)
            return;

        var sky = vm.ConstellationVM;

        _drawable.Events = sky.Events;
        _drawable.Asterism = sky.Asterism;
        // Before Place: the signature shapes the wave the stars are placed against.
        _drawable.Signature = sky.Signature;
        _drawable.WorldWidth = width;
        _drawable.Stars = ConstellationLayout.Place(sky.Events, sky.From, sky.To, width, height, sky.Signature);
        _drawable.SelectedIndex = sky.SelectedIndex;

        ApplyCamera();
    }

    /// <summary>Push the zoom and the scroll to the canvas and repaint. This is all a
    /// zoom step does — no query, no re-place, no re-sort.</summary>
    private void ApplyCamera()
    {
        var width = SkyHost.Width;
        if (width <= 0)
            return;

        var sky = vm.ConstellationVM;

        _scrollX = ClampScroll(_scrollX, width);
        _drawable.Zoom = _zoom;
        _drawable.ScrollX = _scrollX;
        // Only the tick STEP depends on the zoom (month names become days as you go
        // in); the positions stay in world units.
        _drawable.Ticks = ConstellationTicks.Build(sky.From, sky.To, width, _zoom);

        Sky.Invalidate();
    }

    /// <summary>The sky can be dragged from its first moment to its last, and no
    /// further — there is nothing either side of the range that was asked for.</summary>
    private double ClampScroll(double scrollX, double viewportWidth) =>
        Math.Clamp(scrollX, 0, Math.Max(0, viewportWidth * _zoom - viewportWidth));

    /// <summary>The furthest in the sky may be zoomed. Tied to the stretch being looked
    /// at, so a week and a year both bottom out with roughly a day across the canvas —
    /// past that there is nothing left to separate, only empty sky to pan through.</summary>
    private double MaxZoom() => Math.Clamp(vm.ConstellationVM.RangeDays / 1.5, 4.0, 240.0);

    // ── Gestures ─────────────────────────────────────────────────────────────────

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        var width = SkyHost.Width;
        if (width <= 0)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartScroll = _scrollX;
                break;

            case GestureStatus.Running:
                // Dragging right moves the sky right, so time runs backwards under the
                // finger — the direction people expect from every map they have used.
                _scrollX = _panStartScroll - e.TotalX;
                ApplyCamera();
                break;
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running)
            return;

        var width = SkyHost.Width;
        if (width <= 0)
            return;

        // Scale is the factor SINCE THE LAST EVENT, so multiplying in is the whole of
        // it (same as the photo cropper's zoom).
        var zoom = Math.Clamp(_zoom * e.Scale, MinZoom, MaxZoom());
        if (Math.Abs(zoom - _zoom) < 0.0001)
            return;

        // Keep the moment under the fingers exactly where it is: find the world x
        // beneath the pinch, change the magnification, then put that same world x back
        // under the same point on screen. Without this the sky slides out from under
        // the hand that is holding it.
        var anchor = e.ScaleOrigin.X * width;
        var worldAtAnchor = (_scrollX + anchor) / _zoom;

        _zoom = zoom;
        _scrollX = worldAtAnchor * _zoom - anchor;

        ApplyCamera();
    }

    private void OnSkyTapped(object? sender, TappedEventArgs e)
    {
        var position = e.GetPosition(SkyHost);
        if (position is not Point point)
            return;

        // Back through the camera: the finger is on the screen, the stars live in the
        // world. The zoom rides along so "near enough to count" stays the same
        // distance under the fingertip however far in it is.
        var worldX = (point.X + _scrollX) / _zoom;
        var index = ConstellationLayout.HitTest(
            _drawable.Stars, worldX, point.Y, TapReach, _zoom);

        // Empty sky clears the selection rather than being ignored: tapping away is
        // how people close things they opened by tapping.
        vm.ConstellationVM.SelectedIndex = index;
    }

    /// <summary>
    /// Share the sky as a picture: capture the night card, frame it on a Felova page,
    /// hand it to the OS.
    ///
    /// <para>The card is captured rather than redrawn offscreen, so what gets shared is
    /// pixel-for-pixel what was on screen — including wherever it happens to be zoomed
    /// and panned to, which is what the owner chose to look at. Only the CARD is in
    /// frame; the legend, which names conditions out loud, deliberately is not (see
    /// <c>ConstellationShare</c>).</para>
    /// </summary>
    private async void OnShareClicked(object? sender, EventArgs e)
    {
        // The share sheet can take a moment to appear; a second tap in that window
        // would capture and compose the whole thing again for nothing.
        if (_sharing)
            return;

        _sharing = true;
        ShareButton.Opacity = 0.45;

        try
        {
            var capture = await SkyHost.CaptureAsync();
            if (capture is null)
                throw new NotSupportedException("this platform captured nothing");

            byte[] sky;
            using (var stream = await capture.OpenReadAsync(ScreenshotFormat.Png))
            using (var buffer = new MemoryStream())
            {
                await stream.CopyToAsync(buffer);
                sky = buffer.ToArray();
            }

            var vmSky = vm.ConstellationVM;
            var path = await ConstellationShare.CreateAsync(
                sky, vmSky.ShareTitle, vmSky.ShareSubtitle, vmSky.PetName);

            if (path is null)
                throw new InvalidOperationException("the picture could not be composed");

            await ConstellationShare.ShareAsync(path, vmSky.ShareTitle);
            vmSky.TrackShared();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Constellation] share failed: {ex}");
            await DisplayAlert(
                LocalizationManager.Instance.GetString("Sky_ShareFailedTitle"),
                LocalizationManager.Instance.GetString("Sky_ShareFailedBody"),
                LocalizationManager.Instance.GetString("Common_Okay"));
        }
        finally
        {
            _sharing = false;
            ShareButton.Opacity = 1;
        }
    }

    private async void OnBackClicked(object? sender, EventArgs e)
    {
        try
        {
            await Navigation.PopAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Constellation] pop failed: {ex}");
        }
    }
}
