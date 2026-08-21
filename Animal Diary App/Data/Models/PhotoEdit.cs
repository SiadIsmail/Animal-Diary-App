namespace Animal_Diary_App.Data.Models;

/// <summary>
/// A photo staged for the editor: an upright, already-downscaled JPEG on disk plus its
/// pixel size. <see cref="IsTemporary"/> separates a freshly picked photo the editor
/// staged for itself (in the cache folder, safe to delete the moment the sheet closes)
/// from a pet's real photo being re-cropped (owned by <c>PetPhotoService</c>, deleted
/// only through the draft bookkeeping in <c>PetViewModel</c>).
///
/// <para>The size is carried rather than re-read per surface because the preview and the
/// encoder must agree on it exactly: the crop window is expressed relative to these
/// numbers, so two different readings of "how big is this photo" would show one crop and
/// write another.</para>
/// </summary>
public sealed record PhotoEditSource(string Path, int Width, int Height, bool IsTemporary);

/// <summary>
/// Everything the photo editor produces: a quarter-turn count and a square crop window.
/// Deliberately nothing but numbers: the editor ViewModel holds one of these and never
/// a bitmap, so the live preview (a <c>GraphicsView</c> drawable) and the final write
/// (SkiaSharp, off the UI thread) are driven from the same values and agree by
/// construction rather than by two implementations being kept in step by hand.
/// </summary>
public readonly record struct PhotoTransform
{
    /// <summary>Clockwise quarter turns, 0..3. Clockwise because that is what a positive
    /// angle already means to both canvases this rides on: Android's
    /// <c>Matrix.PostRotate</c>, which <c>PetPhotoService</c>'s EXIF path relies on, and
    /// SkiaSharp's <c>RotateDegrees</c>.</summary>
    public int QuarterTurns { get; init; }

    /// <summary>1 = the crop square is exactly the rotated photo's shorter edge (cover
    /// fit, so the square is always full of photo); higher zooms in. Capped at
    /// <see cref="PhotoCrop.MaxZoom"/>.</summary>
    public float Zoom { get; init; }

    /// <summary>Where the photo's centre sits relative to the crop square's centre, as a
    /// FRACTION OF THE SQUARE'S SIDE. Resolution-independent on purpose: the preview
    /// measures in device-independent units and the encoder in pixels, and a fraction
    /// means the same thing to both.</summary>
    public float OffsetX { get; init; }

    /// <inheritdoc cref="OffsetX"/>
    public float OffsetY { get; init; }

    /// <summary>Unrotated, un-zoomed, centred: what the editor opens on.</summary>
    public static PhotoTransform Identity => new() { Zoom = 1f };
}

/// <summary>
/// The crop geometry, written once. Both the preview drawable and the JPEG encoder ask
/// this class for their numbers, so "what the owner saw" and "what got written" cannot
/// drift apart.
///
/// <para>The model: rotate the photo by <see cref="PhotoTransform.QuarterTurns"/>, then
/// take a square window whose side is the rotated photo's shorter edge divided by the
/// zoom, moved by the offsets. Everything below is that sentence in arithmetic.</para>
/// </summary>
public static class PhotoCrop
{
    /// <summary>Zoom floor. Below 1 the crop square would need pixels the photo doesn't
    /// have, so the square could not stay full.</summary>
    public const float MinZoom = 1f;

    /// <summary>Zoom ceiling. The editable copy is ~1600px on its longest edge and the
    /// avatar is written at 800px square, so 4x is the point where the crop stops having
    /// a pixel per output pixel. Past it the owner would only be enlarging blur.</summary>
    public const float MaxZoom = 4f;

    /// <summary>The photo's size after its quarter turns: width and height swap on the
    /// odd ones.</summary>
    public static (int Width, int Height) RotatedSize(int width, int height, int quarterTurns)
        => (quarterTurns & 1) == 1 ? (height, width) : (width, height);

    /// <summary>The crop square's side, measured in the ROTATED photo's own pixels.</summary>
    public static float CropSide(int width, int height, PhotoTransform transform)
    {
        var (w, h) = RotatedSize(width, height, transform.QuarterTurns);
        return Math.Min(w, h) / Math.Clamp(transform.Zoom, MinZoom, MaxZoom);
    }

    /// <summary>How far the photo may be dragged before the crop square would run off it,
    /// in the same fraction-of-the-side units the offsets use. Zero on the axis the cover
    /// fit already pins (at zoom 1 the shorter edge has no slack).</summary>
    public static (float X, float Y) MaxOffset(int width, int height, PhotoTransform transform)
    {
        var (w, h) = RotatedSize(width, height, transform.QuarterTurns);
        var side = CropSide(width, height, transform);
        if (side <= 0)
            return (0f, 0f);

        return (Math.Max(0f, (w / side - 1f) / 2f), Math.Max(0f, (h / side - 1f) / 2f));
    }

    /// <summary>Pull a transform back into range: zoom into [1, <see cref="MaxZoom"/>] and
    /// the offsets far enough in that the crop square stays completely covered by photo.
    /// The single place either rule is written: a gesture, a quarter turn and the encoder
    /// all come through here, so none of them can invent an empty corner.</summary>
    public static PhotoTransform Clamp(int width, int height, PhotoTransform transform)
    {
        // Zoom first: the offset limits are derived from the crop side, which is derived
        // from the zoom, so clamping them in the other order would measure the slack
        // against a zoom that is about to change.
        var clamped = transform with { Zoom = Math.Clamp(transform.Zoom, MinZoom, MaxZoom) };
        var (maxX, maxY) = MaxOffset(width, height, clamped);

        return clamped with
        {
            OffsetX = Math.Clamp(transform.OffsetX, -maxX, maxX),
            OffsetY = Math.Clamp(transform.OffsetY, -maxY, maxY),
        };
    }

    /// <summary>The scale that takes source pixels to output units, for a crop square of
    /// <paramref name="side"/> units: screen units in the preview, output pixels in the
    /// file. Same function, two unit systems, which is what makes the preview honest.</summary>
    public static float Scale(int width, int height, PhotoTransform transform, float side)
    {
        var (w, h) = RotatedSize(width, height, transform.QuarterTurns);
        return side / Math.Min(w, h) * Math.Clamp(transform.Zoom, MinZoom, MaxZoom);
    }

    /// <summary>Turn the photo a quarter turn clockwise, carrying the crop with it.
    ///
    /// <para>Rotating the offset vector by the same quarter turn keeps EXACTLY the same
    /// content inside the mask, because the mask is a circle and a circle is unchanged by
    /// rotation. So "turn right" reads as the photo spinning under a fixed window rather
    /// than as the crop jumping somewhere else, which is what re-centring would do, and
    /// it would undo the owner's positioning every time they fixed the orientation.</para></summary>
    public static PhotoTransform TurnClockwise(int width, int height, PhotoTransform transform) =>
        Clamp(width, height, transform with
        {
            QuarterTurns = (transform.QuarterTurns + 1) & 3,
            // Clockwise in screen coordinates (y grows downward): (x, y) → (-y, x).
            OffsetX = -transform.OffsetY,
            OffsetY = transform.OffsetX,
        });

    /// <inheritdoc cref="TurnClockwise"/>
    public static PhotoTransform TurnCounterClockwise(int width, int height, PhotoTransform transform) =>
        Clamp(width, height, transform with
        {
            QuarterTurns = (transform.QuarterTurns + 3) & 3,
            OffsetX = transform.OffsetY,
            OffsetY = -transform.OffsetX,
        });
}
