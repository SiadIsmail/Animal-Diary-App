namespace Animal_Diary_App.Data.ViewModels;

using System.Windows.Input;
using Animal_Diary_App.Data.Models;

/// <summary>
/// The crop-and-rotate editor a photo passes through on its way to becoming a pet's
/// avatar, on the shared <c>FelovaBottomSheet</c> like every other input.
///
/// <para>Rotation is the point of it. Camera photos still arrive a quarter-turn wrong on
/// some devices (some camera apps rotate the pixels themselves AND leave a non-1 EXIF
/// orientation tag, so applying the tag turns them a second time). Whatever the app gets
/// wrong there, the owner now sees it in the preview and fixes it in one tap, which
/// turns a bug we cannot reproduce on demand into an inconvenience.</para>
///
/// <para>This holds the transform and NOTHING ELSE, no bitmap, no stream, no file
/// handle. The pixels stay with <c>PetPhotoService</c> on both ends: it stages the
/// editable copy and it writes the result. What travels between them is four numbers.</para>
///
/// <para>Awaited rather than event-driven, following <see cref="ConfirmSheetViewModel"/>:
/// the caller is a page code-behind mid-way through a pick, and "wait for the answer"
/// reads far better there than a <c>Saved</c> handler holding the other half of the flow.</para>
/// </summary>
public sealed class PhotoEditorSheetViewModel : BaseViewModel
{
    private TaskCompletionSource<PhotoTransform?>? _pending;

    public PhotoEditorSheetViewModel()
    {
        TurnLeftCommand = new Command(() => Transform = Turn(clockwise: false));
        TurnRightCommand = new Command(() => Transform = Turn(clockwise: true));
        DoneCommand = new Command(Done);
        DismissCommand = new Command(() => IsPresented = false);
    }

    private bool _isPresented;
    public bool IsPresented
    {
        get => _isPresented;
        set
        {
            // Closing by ANY route answers the caller: the Done button, a scrim tap,
            // Android back, or a page tearing the sheet down. Without this an awaiting
            // pick would hang forever on a sheet the owner had already dismissed.
            if (SetProperty(ref _isPresented, value) && !value)
                Complete(null);
        }
    }

    /// <summary>The staged photo being edited: its path, for the preview to load, and its
    /// pixel size, which every offset and zoom limit is measured against.</summary>
    public PhotoEditSource? Source { get; private set; }

    private PhotoTransform _transform = PhotoTransform.Identity;

    /// <summary>The whole output of this sheet. The view redraws on its
    /// <c>PropertyChanged</c>; nothing else about the editor is state.</summary>
    public PhotoTransform Transform
    {
        get => _transform;
        private set => SetProperty(ref _transform, value);
    }

    public ICommand TurnLeftCommand { get; }
    public ICommand TurnRightCommand { get; }
    public ICommand DoneCommand { get; }
    public ICommand DismissCommand { get; }

    /// <summary>
    /// Present the editor for a staged photo and wait for it. Returns the transform to
    /// apply, or null if the owner backed out: backing out is always a valid answer, and
    /// it means the pet's photo does not change at all.
    /// </summary>
    public Task<PhotoTransform?> EditAsync(PhotoEditSource source)
    {
        // A second edit while one is still open resolves the first as cancelled rather
        // than abandoning its caller mid-await.
        Complete(null);

        Source = source;
        Transform = PhotoTransform.Identity;
        OnPropertyChanged(nameof(Source));

        var pending = new TaskCompletionSource<PhotoTransform?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending = pending;
        IsPresented = true;
        return pending.Task;
    }

    /// <summary>Drag: place the photo's centre relative to the crop square's, in fractions
    /// of the square's side. Clamped so the square never leaves the photo.</summary>
    public void SetOffset(float x, float y) =>
        Transform = Clamped(Transform with { OffsetX = x, OffsetY = y });

    /// <summary>Pinch: multiply the zoom by the gesture's incremental factor. Clamped to
    /// [1, <see cref="PhotoCrop.MaxZoom"/>], and re-clamping the offsets with it, because
    /// zooming back out shrinks how far the photo is allowed to be dragged.</summary>
    public void MultiplyZoom(float factor)
    {
        if (factor <= 0 || float.IsNaN(factor))
            return; // a degenerate gesture value would wipe the zoom out entirely

        Transform = Clamped(Transform with { Zoom = Transform.Zoom * factor });
    }

    private void Done()
    {
        // Resolve BEFORE closing: the IsPresented setter completes any still-pending edit
        // as cancelled, so closing first would throw the answer away.
        Complete(Transform);
        IsPresented = false;
    }

    private PhotoTransform Turn(bool clockwise)
    {
        if (Source is not { } source)
            return Transform;

        return clockwise
            ? PhotoCrop.TurnClockwise(source.Width, source.Height, Transform)
            : PhotoCrop.TurnCounterClockwise(source.Width, source.Height, Transform);
    }

    private PhotoTransform Clamped(PhotoTransform transform) =>
        Source is { } source ? PhotoCrop.Clamp(source.Width, source.Height, transform) : transform;

    private void Complete(PhotoTransform? transform)
    {
        var pending = _pending;
        _pending = null;
        pending?.TrySetResult(transform);
    }
}
