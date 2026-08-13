namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using System.Diagnostics;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.View.Controls;
using Animal_Diary_App.Data.ViewModels;

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

    /// <summary>1 = the whole chosen stretch fits the canvas. Zooming in reveals more
    /// individual stars rather than magnifying a summary — there is no summary.</summary>
    private double _zoom = 1;
    private double _scrollX;

    /// <summary>Where the sky sat when the current drag began. A pan reports its total
    /// travel since the finger went down, so the offset is always start + total — never
    /// an accumulation, which drifts over a long drag.</summary>
    private double _panStartScroll;

    private bool _tracked;

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
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(ConstellationViewModel.SelectedIndex))
            return;

        _drawable.SelectedIndex = vm.ConstellationVM.SelectedIndex;
        Sky.Invalidate();
    }

    // ── Placement ────────────────────────────────────────────────────────────────

    /// <summary>Re-place every star and repaint. Runs on layout, on a new range and on
    /// every zoom step — placement depends on the content width, and the content width
    /// depends on the zoom, so it cannot be cached across one.</summary>
    private void Rebuild()
    {
        var width = SkyHost.Width;
        var height = SkyHost.Height;
        if (width <= 0 || height <= 0)
            return;

        var sky = vm.ConstellationVM;
        var contentWidth = width * _zoom;

        _drawable.Events = sky.Events;
        _drawable.ContentWidth = contentWidth;
        _drawable.Stars = ConstellationLayout.Place(sky.Events, sky.From, sky.To, contentWidth, height);
        _drawable.Ticks = ConstellationTicks.Build(sky.From, sky.To, contentWidth);
        _drawable.SelectedIndex = sky.SelectedIndex;

        _scrollX = ClampScroll(_scrollX, contentWidth, width);
        _drawable.ScrollX = _scrollX;

        Sky.Invalidate();
    }

    private static double ClampScroll(double scrollX, double contentWidth, double viewportWidth) =>
        Math.Clamp(scrollX, 0, Math.Max(0, contentWidth - viewportWidth));

    /// <summary>The furthest in the sky may be zoomed. Tied to the stretch on screen so
    /// a week and a year both bottom out at roughly the same handful of hours across
    /// the canvas — otherwise a week zooms to a single minute of empty space.</summary>
    private double MaxZoom() => Math.Clamp(vm.ConstellationVM.RangeDays * 3.0, 4.0, 120.0);

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
                _scrollX = ClampScroll(_panStartScroll - e.TotalX, width * _zoom, width);
                _drawable.ScrollX = _scrollX;
                Sky.Invalidate();
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

        // Keep whatever moment sits under the pinch where it is. Content x scales with
        // the zoom, so the fraction of the whole stretch under the fingers is the thing
        // that has to survive — pin that and the sky grows around it instead of
        // sliding out from under them.
        var anchor = e.ScaleOrigin.X * width;
        var fraction = (_scrollX + anchor) / (width * _zoom);

        _zoom = zoom;
        _scrollX = ClampScroll(fraction * width * _zoom - anchor, width * _zoom, width);

        Rebuild();
    }

    private void OnSkyTapped(object? sender, TappedEventArgs e)
    {
        var position = e.GetPosition(SkyHost);
        if (position is not Point point)
            return;

        // The canvas and the content share one unit, so the only difference between
        // where the finger is and where the star is, is how far the sky has been
        // dragged.
        var index = ConstellationLayout.HitTest(
            _drawable.Stars, point.X + _scrollX, point.Y, TapReach);

        // Empty sky clears the selection rather than being ignored: tapping away is
        // how people close things they opened by tapping.
        vm.ConstellationVM.SelectedIndex = index;
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
