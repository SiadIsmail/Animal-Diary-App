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
    private double _panStartPeriod;

    /// <summary>Canvas units of drag per day of fold. Loose enough that a whole
    /// month's worth of periods is one comfortable sweep of the thumb.</summary>
    private const double DaysPerDragUnit = 16.0;

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
        SkyHost.SizeChanged += (_, _) =>
        {
            Rebuild();
            if (_revealPending)
                PlayReveal();
        };
    }

    /// <summary>Android back clears the tapped entry before it navigates — the peek is
    /// in the card now rather than in an overlay, so nothing else would close it.</summary>
    protected override bool OnBackButtonPressed()
    {
        if (vm.ConstellationVM.HasSelection)
        {
            vm.ConstellationVM.SelectedIndex = -1;
            return true;
        }

        // No BackDismiss walk: this page hosts no overlay any more — the peek is a card
        // inside the sky — so there is nothing for it to find.
        return base.OnBackButtonPressed();
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.ConstellationVM.SkyChanged += OnSkyChanged;
        vm.ConstellationVM.ViewChanged += OnViewChanged;
        vm.ConstellationVM.RepaintRequested += OnRepaintRequested;
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
        vm.ConstellationVM.ViewChanged -= OnViewChanged;
        vm.ConstellationVM.RepaintRequested -= OnRepaintRequested;
        this.AbortAnimation(FlightName);
        this.AbortAnimation(RevealName);
        this.AbortAnimation(SelectionName);
        this.AbortAnimation(BloomName);
        vm.ConstellationVM.PropertyChanged -= OnViewModelPropertyChanged;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;

        // The ViewModel is a singleton, so this visit's exploration would otherwise be
        // waiting here next time — including the flag that suppresses the opening focus.
        vm.ConstellationVM.EndVisit();
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
        Settle();
        PlayReveal();
    }

    /// <summary>Set when new data lands, cleared once the reveal has actually run.
    ///
    /// <para>On the first visit the entries arrive before the card has been measured,
    /// so there is nothing placed to animate and the reveal was being spent on an empty
    /// canvas — the one time it most wanted to be seen. It now waits for the first
    /// layout that produces stars.</para></summary>
    private bool _revealPending;

    /// <summary>The sky writes itself left to right. The stagger lives in the drawable
    /// (it knows where each star sits); this only drives the clock.</summary>
    private void PlayReveal()
    {
        this.AbortAnimation(RevealName);

        if (ReducedMotion.IsEnabled)
        {
            _revealPending = false;
            _drawable.Reveal = 1;
            Sky.Invalidate();
            return;
        }

        if (_drawable.Stars.Count == 0)
        {
            // Nothing placed yet: hold the sky back rather than burning the reveal on
            // an empty canvas, and run it the moment the first real layout lands.
            _revealPending = true;
            _drawable.Reveal = 0;
            return;
        }

        _revealPending = false;
        _drawable.Reveal = 0;
        new Animation(v => { _drawable.Reveal = v; Sky.Invalidate(); }, 0, 1, Easing.CubicOut)
            .Commit(this, RevealName, length: RevealMilliseconds);
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
        if (width <= 0 || stars.Count < 2 || vm.ConstellationVM.Lens != SkyLens.History)
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

    /// <summary>A lens or a fold changed. Nothing was re-read; the same entries are
    /// simply arranged another way — so the stars fly to their new places rather than
    /// cutting there.</summary>
    private void OnViewChanged()
    {
        var before = Snapshot();

        Rebuild();
        FitToData();

        MarkSelectedDay();
        FlyTo(before);
    }

    /// <summary>Only the focus changed, which is a matter of who is bright — no entry
    /// moved. Re-placing here is what used to throw the wall back to its bottom and
    /// reset the Timeline's zoom every time someone tapped a legend key.</summary>
    private void OnRepaintRequested()
    {
        _drawable.Focus = vm.ConstellationVM.Focus;
        PlayBloom();
    }

    /// <summary>Focus is a transformation, not a dimmer switch: the unfocused recede
    /// while the chosen kind blooms and chains. Animating it is what makes it read as
    /// bringing something forward rather than turning the rest off.</summary>
    private void PlayBloom()
    {
        this.AbortAnimation(BloomName);

        if (ReducedMotion.IsEnabled)
        {
            _drawable.Bloom = 1;
            Sky.Invalidate();
            return;
        }

        _drawable.Bloom = 0;
        new Animation(v => { _drawable.Bloom = v; Sky.Invalidate(); }, 0, 1, Easing.CubicOut)
            .Commit(this, BloomName, length: BloomMilliseconds);
    }

    // ── The flight between lenses ────────────────────────────────────────────────

    private const uint FlightMilliseconds = 620;
    private const uint FoldMilliseconds = 260;
    private const string FlightName = "sky.lens";

    // ── The three moments ────────────────────────────────────────────────────────
    // Everything expressive on this surface is a RESPONSE, never wallpaper: the
    // resting picture stays plain enough to read, and the beauty is what arriving,
    // touching and focusing feel like. Each is skipped whole when the OS asks for
    // reduced motion — the page background already pays that courtesy.
    private const uint RevealMilliseconds = 1150;
    private const uint SelectionMilliseconds = 430;
    private const uint BloomMilliseconds = 340;
    private const string RevealName = "sky.reveal";
    private const string SelectionName = "sky.select";
    private const string BloomName = "sky.bloom";

    /// <summary>
    /// Where every star is on the canvas <b>right now</b> — the "from" end of a
    /// flight, taken before anything is re-placed.
    ///
    /// <para>If a flight is already in the air it reads the interpolated positions
    /// rather than the last target. That is what lets turning the fold dial chain: each
    /// step sets off from wherever the stars actually are, so a scrub is one continuous
    /// drift instead of a series of jumps back to the previous answer.</para>
    /// </summary>
    private (PointF[] Points, SkyLens Lens) Snapshot()
    {
        var stars = _drawable.Stars;
        var points = new PointF[stars.Count];

        var mid = _drawable.Transition < 1
            && _drawable.TweenFrom.Count == stars.Count
            && _drawable.TweenTo.Count == stars.Count;

        for (int i = 0; i < stars.Count; i++)
        {
            points[i] = mid
                // The animation hands back already-eased values, so this is simply
                // where the star is on screen at this instant.
                ? Between(_drawable.TweenFrom[i], _drawable.TweenTo[i], (float)_drawable.Transition)
                : new PointF(StarScreenX(stars[i]), StarScreenY(stars[i]));
        }

        return (points, _drawable.Lens);

        static PointF Between(PointF a, PointF b, float t) =>
            new(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);
    }

    /// <summary>
    /// Fly from a snapshot to wherever the stars have just been re-placed.
    ///
    /// <para>The flight is skipped when there is nothing to compare (a different number
    /// of entries, i.e. this was a reload rather than a re-arrangement) and when the OS
    /// has been asked to reduce motion — the same courtesy the page background
    /// already pays.</para>
    /// </summary>
    private void FlyTo((PointF[] Points, SkyLens Lens) before)
    {
        this.AbortAnimation(FlightName);

        var stars = _drawable.Stars;
        if (before.Points.Length != stars.Count || stars.Count == 0 || ReducedMotion.IsEnabled)
        {
            Settle();
            return;
        }

        // A lens change is a journey and gets the full arc. Turning the fold is a
        // nudge — the same stars rearranging on the same ring — so it moves quickly
        // enough to keep up with a thumb, and slowly enough to be followed.
        var length = before.Lens == _drawable.Lens ? FoldMilliseconds : FlightMilliseconds;

        var to = new PointF[stars.Count];
        for (int i = 0; i < stars.Count; i++)
            to[i] = new PointF(StarScreenX(stars[i]), StarScreenY(stars[i]));

        _drawable.FromLens = before.Lens;
        _drawable.TweenFrom = before.Points;
        _drawable.TweenTo = to;
        _drawable.Transition = 0;

        new Animation(v =>
        {
            _drawable.Transition = v;
            Sky.Invalidate();
        }, 0, 1, Easing.CubicInOut)
        .Commit(this, FlightName, length: length, finished: (_, _) => Settle());
    }

    /// <summary>Land: nothing in flight, the arriving lens drawn normally.</summary>
    private void Settle()
    {
        _drawable.Transition = 1;
        _drawable.FromLens = _drawable.Lens;
        _drawable.TweenFrom = Array.Empty<PointF>();
        _drawable.TweenTo = Array.Empty<PointF>();
        Sky.Invalidate();
    }

    private float StarScreenX(in SkyStar star) => (float)(
        _drawable.Lens == SkyLens.History
            ? star.X * _zoom - _scrollX + ConstellationLayout.HourGutter
            : star.X);

    private float StarScreenY(in SkyStar star) => (float)star.Y;


    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConstellationViewModel.SelectedIndex))
            return;

        _drawable.SelectedIndex = vm.ConstellationVM.SelectedIndex;
        MarkSelectedDay();
        PlaceDetailCard();
        PlaySelection();
    }

    /// <summary>
    /// Which slice of time the open sheet is talking about.
    ///
    /// <para>The column and the brightened siblings are "Also that day" drawn on the
    /// canvas — the same entries the sheet lists, shown where they happened. The span
    /// is computed here because the page is what knows the range.</para>
    /// </summary>
    private void MarkSelectedDay()
    {
        var sky = vm.ConstellationVM;
        var index = sky.SelectedIndex;

        if (index < 0 || index >= sky.Events.Count)
        {
            _drawable.HighlightDay = null;
            return;
        }

        var day = sky.Events[index].When.Date;
        var world = Math.Max(1, SkyHost.Width - ConstellationLayout.HourGutter);

        _drawable.HighlightDay = day;
        _drawable.HighlightFromX = ConstellationLayout.XFor(day, sky.From, sky.To, world);
        _drawable.HighlightToX = ConstellationLayout.XFor(day.AddDays(1), sky.From, sky.To, world);
    }

    /// <summary>Put the peek on the opposite half of the card from the star it
    /// describes, so it never covers the thing it is pointing at.</summary>
    private void PlaceDetailCard()
    {
        var sky = vm.ConstellationVM;
        var index = sky.SelectedIndex;
        var height = SkyHost.Height;

        if (index < 0 || index >= _drawable.Stars.Count || height <= 0)
            return;

        DetailCard.VerticalOptions = _drawable.Stars[index].Y > height / 2
            ? LayoutOptions.Start
            : LayoutOptions.End;
    }

    private void PlaySelection()
    {
        this.AbortAnimation(SelectionName);

        if (ReducedMotion.IsEnabled || _drawable.HighlightDay is null)
        {
            _drawable.Selection = 1;
            Sky.Invalidate();
            return;
        }

        _drawable.Selection = 0;
        new Animation(v => { _drawable.Selection = v; Sky.Invalidate(); }, 0, 1, Easing.CubicOut)
            .Commit(this, SelectionName, length: SelectionMilliseconds);
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
        // Before Place: the signature shapes the wave the stars are placed against.
        _drawable.Signature = sky.Signature;
        _drawable.Lens = sky.Lens;
        _drawable.Focus = sky.Focus;
        _drawable.SelectedIndex = sky.SelectedIndex;

        // The lens decides what the axis means. Only the Timeline keeps a camera —
        // a clock is not panned, and a fold is exactly one turn wide.
        // The plot sits past the hour gutter, so the world is that much narrower than
        // the card — the gutter is screen furniture and must not scroll with the dates.
        var world = Math.Max(1, width - ConstellationLayout.HourGutter);
        _drawable.WorldWidth = world;

        _drawable.Stars = sky.Lens == SkyLens.Cycle
            ? ConstellationLayout.PlaceOnRing(sky.Events, sky.From, sky.To, sky.PeriodDays, width, height)
            : ConstellationLayout.PlaceOnGrid(sky.Events, sky.From, sky.To, world, height);

        if (sky.Lens != SkyLens.History)
        {
            _zoom = MinZoom;
            _scrollX = 0;
        }

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
        var height = SkyHost.Height;

        _scrollX = ClampScroll(_scrollX, width);
        _drawable.Zoom = _zoom;
        _drawable.ScrollX = _scrollX;

        _drawable.Ticks = sky.Lens == SkyLens.Cycle
            ? ConstellationTicks.Ring(sky.PeriodDays, width, height)
            : ConstellationTicks.Grid(sky.From, sky.To, Math.Max(1, width - ConstellationLayout.HourGutter), height, _zoom);

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
        var lens = vm.ConstellationVM.Lens;
        if (width <= 0)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStartScroll = _scrollX;
                _panStartPeriod = vm.ConstellationVM.PeriodDays;
                break;

            case GestureStatus.Running:
                if (lens == SkyLens.Cycle)
                {
                    // A ring has nowhere to be panned to, so the drag does the thing the
                    // lens is actually for: it TURNS THE FOLD. Reaching for the whole
                    // canvas to look for a rhythm is a far more direct way to ask the
                    // question than nudging a slider under it.
                    vm.ConstellationVM.PeriodDays = _panStartPeriod + e.TotalX / DaysPerDragUnit;
                }
                else
                {
                    // Dragging right moves the sky right, so time runs backwards under
                    // the finger — the direction people expect from every map.
                    _scrollX = _panStartScroll - e.TotalX;
                    ApplyCamera();
                }

                break;
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        if (e.Status != GestureStatus.Running)
            return;

        var width = SkyHost.Width;
        if (width <= 0 || vm.ConstellationVM.Lens != SkyLens.History)
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
        // Back through the camera: the finger is on the screen, the stars live in the
        // world — and on History the plot starts past the hour gutter.
        var worldX = vm.ConstellationVM.Lens == SkyLens.History
            ? (point.X - ConstellationLayout.HourGutter + _scrollX) / _zoom
            : point.X;

        var index = ConstellationLayout.HitTest(
            _drawable.Stars, worldX, point.Y, TapReach,
            vm.ConstellationVM.Lens == SkyLens.History ? _zoom : 1);

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
