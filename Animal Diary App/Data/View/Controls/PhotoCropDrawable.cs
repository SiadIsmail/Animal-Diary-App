namespace Animal_Diary_App.Data.View.Controls;

using Animal_Diary_App.Data.Models;
using Microsoft.Maui.Graphics;

/// <summary>
/// The photo editor's live preview: the photo under a circular mask, drawn on a plain
/// <c>GraphicsView</c> (the <see cref="ProgressRingView"/> / <c>WeightChartDrawable</c>
/// precedent). No third-party cropper, and nothing here ships a native library.
///
/// <para>The mask is a CIRCLE because the destination is one — <see cref="PetAvatarView"/>
/// is round everywhere it appears. The square crop is what gets written; the circle is
/// what the owner will actually see of it, so it is what they should be aiming with.</para>
///
/// <para>The transform chain in <see cref="Draw"/> is the same sequence, in the same
/// order, as <c>PetPhotoService.Crop</c>, with the crop square's side as the only thing
/// that differs (screen units here, output pixels there). Both take their numbers from
/// <see cref="PhotoCrop"/>. Change one and you must change the other, or the preview
/// starts lying.</para>
/// </summary>
public sealed class PhotoCropDrawable : IDrawable
{
    // Breathing room so the mask ring isn't flush against the stage edge.
    private const float Inset = 10f;

    /// <summary>The staged photo's pixels. Null while one is loading — the stage draws
    /// its backdrop and nothing else rather than flashing a half-loaded image.</summary>
    public IImage? Image { get; set; }

    /// <summary>The photo's pixel size as the ViewModel knows it. Taken from the source
    /// record rather than from <see cref="Image"/> so the preview measures against exactly
    /// the numbers the encoder will, even if a platform decoder returns something else.</summary>
    public int SourceWidth { get; set; }

    /// <inheritdoc cref="SourceWidth"/>
    public int SourceHeight { get; set; }

    public PhotoTransform Transform { get; set; } = PhotoTransform.Identity;

    /// <summary>Dims everything outside the circle. Set from the palette by the host.</summary>
    public Color Scrim { get; set; } = Colors.Black.WithAlpha(0.6f);

    /// <summary>The ring around the crop circle.</summary>
    public Color Outline { get; set; } = Colors.White;

    /// <summary>Behind the photo — visible as bars beside a portrait shot at zoom 1.</summary>
    public Color Backdrop { get; set; } = Colors.Black;

    /// <summary>The crop square: the largest centred square the stage can hold. Public and
    /// static because the gesture handlers need the same side length to turn a drag in
    /// screen units into the fraction-of-the-side offsets the transform speaks in.</summary>
    public static RectF FrameFor(RectF rect)
    {
        var side = Math.Max(0f, Math.Min(rect.Width, rect.Height) - Inset * 2);
        return new RectF(rect.Center.X - side / 2f, rect.Center.Y - side / 2f, side, side);
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        canvas.FillColor = Backdrop;
        canvas.FillRectangle(rect);

        var frame = FrameFor(rect);
        if (frame.Width <= 0)
            return;

        if (Image is { } image && SourceWidth > 0 && SourceHeight > 0)
        {
            canvas.SaveState();

            // Offsets are a fraction of the crop square's side, so they scale with the
            // stage: the same transform previews identically on any screen size and
            // encodes identically at 800px.
            canvas.Translate(
                frame.Center.X + Transform.OffsetX * frame.Width,
                frame.Center.Y + Transform.OffsetY * frame.Width);
            canvas.Rotate(90f * Transform.QuarterTurns);

            var scale = PhotoCrop.Scale(SourceWidth, SourceHeight, Transform, frame.Width);
            canvas.Scale(scale, scale);

            // Drawn at the SOURCE size and scaled by the shared factor, rather than at
            // whatever the decoded image reports — same reason SourceWidth exists.
            canvas.DrawImage(image, -SourceWidth / 2f, -SourceHeight / 2f, SourceWidth, SourceHeight);

            canvas.RestoreState();
        }

        // The mask: one path holding the whole stage and the crop circle, filled even-odd
        // so the circle punches a hole. Cheaper and cleaner than clipping and painting the
        // four rectangles around a round hole.
        var mask = new PathF();
        mask.AppendRectangle(rect);
        mask.AppendCircle(frame.Center.X, frame.Center.Y, frame.Width / 2f);
        canvas.FillColor = Scrim;
        canvas.FillPath(mask, WindingMode.EvenOdd);

        canvas.StrokeColor = Outline;
        canvas.StrokeSize = 1.5f;
        canvas.DrawCircle(frame.Center.X, frame.Center.Y, frame.Width / 2f);
    }
}
