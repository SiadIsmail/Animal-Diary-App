namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using System.Diagnostics;
using Animal_Diary_App.Data.View.Controls;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Graphics.Platform;

/// <summary>
/// The photo editor's view half. It owns the one thing the ViewModel deliberately does
/// not: the decoded bitmap. The ViewModel holds four numbers, this holds the pixels to
/// preview them against, and the file on disk is owned by <c>PetPhotoService</c> — so
/// nothing anywhere holds a photo it isn't the right layer to hold.
/// </summary>
public partial class PhotoEditorSheetView : ContentView
{
    private readonly PhotoCropDrawable _drawable = new();
    private PhotoEditorSheetViewModel? _vm;

    // Where the photo sat when the current drag began. A pan reports its total travel
    // since the finger went down, so the offset is always start + total — never an
    // accumulation, which would drift over a long drag.
    private (float X, float Y) _panStart;

    public PhotoEditorSheetView()
    {
        InitializeComponent();

        // Palette rather than hex literals: a drawable can't reach a StaticResource, so
        // it comes through AppColors (see known-constraints.md on code-side colours).
        _drawable.Backdrop = AppColors.Resolve("Ink", Colors.Black);
        _drawable.Scrim = AppColors.Resolve("Ink", Colors.Black).WithAlpha(0.66f);
        _drawable.Outline = AppColors.Resolve("Washi", Colors.White);

        Surface.Drawable = _drawable;

        // The ViewModel is a singleton and this view is rebuilt every time the create/edit
        // pet page is pushed, so the subscription is scoped to being on screen. Left on
        // BindingContextChanged alone, every page instance ever pushed would stay attached
        // and each would decode the same photo again on the next edit, holding its own
        // several-megabyte bitmap for the life of the process.
        BindingContextChanged += (_, _) => Rebind();
        Loaded += (_, _) => Rebind();
        Unloaded += (_, _) => Detach();
    }

    private void Rebind()
    {
        Detach();

        if (!IsLoaded)
            return;

        _vm = BindingContext as PhotoEditorSheetViewModel;
        if (_vm is not null)
            _vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void Detach()
    {
        if (_vm is not null)
            _vm.PropertyChanged -= OnViewModelPropertyChanged;
        _vm = null;

        // Nothing off screen keeps a photo in memory.
        _drawable.Image?.Dispose();
        _drawable.Image = null;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_vm is null)
            return;

        if (e.PropertyName == nameof(PhotoEditorSheetViewModel.Source))
        {
            LoadSourceAsync().Forget();
        }
        else if (e.PropertyName == nameof(PhotoEditorSheetViewModel.Transform))
        {
            _drawable.Transform = _vm.Transform;
            Surface.Invalidate();
        }
    }

    /// <summary>Decode the staged photo off the UI thread and hand it to the drawable. A
    /// blank stage for a moment is the right failure mode here — the sheet is already
    /// sliding in, and blocking the slide to decode a 1600px JPEG would be worse.</summary>
    private async Task LoadSourceAsync()
    {
        var source = _vm?.Source;

        _drawable.Image?.Dispose();
        _drawable.Image = null;
        _drawable.SourceWidth = source?.Width ?? 0;
        _drawable.SourceHeight = source?.Height ?? 0;
        _drawable.Transform = _vm?.Transform ?? Models.PhotoTransform.Identity;
        Surface.Invalidate();

        if (source is null)
            return;

        try
        {
            var image = await Task.Run(() =>
            {
                using var stream = File.OpenRead(source.Path);
                return PlatformImage.FromStream(stream);
            });

            // A newer edit may own the stage by now (the owner picked again while this
            // was decoding); that one's bitmap must not be replaced by this one's.
            if (!ReferenceEquals(_vm?.Source, source))
            {
                image.Dispose();
                return;
            }

            _drawable.Image = image;
            Surface.Invalidate();
        }
        catch (Exception ex)
        {
            // The staged file is unreadable. The sheet stays usable and "Use this photo"
            // will fail the same way in the service, which the page reports.
            Debug.WriteLine($"[PhotoEdit] preview decode failed: {ex}");
        }
    }

    // ── Gestures ─────────────────────────────────────────────────────────────────

    private void OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (_vm is null)
            return;

        // The drag arrives in device-independent units and the transform speaks in
        // fractions of the crop square's side, so every drag is divided by that side —
        // which is also what makes the same gesture mean the same thing on any screen.
        var side = CropSide();
        if (side <= 0)
            return;

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _panStart = (_vm.Transform.OffsetX, _vm.Transform.OffsetY);
                break;

            case GestureStatus.Running:
                _vm.SetOffset(
                    _panStart.X + (float)(e.TotalX / side),
                    _panStart.Y + (float)(e.TotalY / side));
                break;
        }
    }

    private void OnPinchUpdated(object? sender, PinchGestureUpdatedEventArgs e)
    {
        // Scale is the factor SINCE THE LAST EVENT, so multiplying in is the whole of it.
        // The zoom is about the crop circle's centre rather than the pinch point: for a
        // round mask that reads as the photo growing under a fixed hole, and it keeps the
        // transform to the four numbers the encoder can reproduce.
        if (_vm is not null && e.Status == GestureStatus.Running)
            _vm.MultiplyZoom((float)e.Scale);
    }

    /// <summary>The crop square's side in the same units the gestures report. Asked of the
    /// drawable so the geometry has one definition, not two.</summary>
    private float CropSide()
    {
        if (Surface.Width <= 0 || Surface.Height <= 0)
            return 0f;

        var stage = new RectF(0f, 0f, (float)Surface.Width, (float)Surface.Height);
        return PhotoCropDrawable.FrameFor(stage).Width;
    }
}
