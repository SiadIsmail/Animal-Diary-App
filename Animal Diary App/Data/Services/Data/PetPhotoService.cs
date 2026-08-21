namespace Animal_Diary_App.Data.Services;

using System.Diagnostics;
using Animal_Diary_App.Data.Models;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;
using SkiaSharp;

/// <summary>
/// Owns pet profile photos on disk: the <c>PetPhotos/</c> folder inside app storage.
/// Everything that writes or removes a photo file goes through here (mirrors the
/// report library's ownership of the <c>Reports/</c> folder). A photo is referenced
/// from <see cref="Models.Pet.PhotoFileName"/> by its RELATIVE name; the file itself
/// is local-only and never synced, so it is deleted outright with the pet / on reset,
/// never tombstoned.
/// </summary>
public class PetPhotoService
{
    // Incoming photos (camera shots especially) can be several megabytes; we don't need
    // that for a small circular avatar. Re-encode down to this longest edge as JPEG so
    // storage stays small and image loading stays fast.
    private const int MaxEdgePixels = 800;
    private const float JpegQuality = 0.85f;

    // The editor works on a bigger copy than it writes. Cropping throws pixels away, so
    // staging at the final 800px would mean a tight crop landing well under it: 1600
    // leaves the whole zoom range (see PhotoCrop.MaxZoom) with a source pixel per output
    // pixel, and the copy is temporary so its extra bytes never accumulate.
    private const int EditEdgePixels = 1600;

    // Bilinear + mipmaps: the crop is almost always a downscale, and nearest-neighbour on
    // a 2x reduction is visibly gritty on a face.
    private static readonly SKSamplingOptions CropSampling =
        new(SKFilterMode.Linear, SKMipmapMode.Linear);

    /// <summary>The folder every pet photo lives in. Created lazily so a fresh install
    /// has no empty directory until the first photo is saved.</summary>
    public static string PhotosDirectory
    {
        get
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "PetPhotos");
            Directory.CreateDirectory(dir); // no-op when it already exists
            return dir;
        }
    }

    /// <summary>Where a photo waits while the owner is cropping it. Deliberately the
    /// CACHE directory, not app data: a staged copy that outlives its sheet (process
    /// death mid-edit) is throwaway, and cache is the one place the OS may reclaim on our
    /// behalf. Keeping it out of <see cref="PhotosDirectory"/> also means that folder only
    /// ever holds real pet photos, so nothing there is ever an orphan to reason about.</summary>
    private static string EditsDirectory
    {
        get
        {
            var dir = Path.Combine(FileSystem.CacheDirectory, "PetPhotoEdits");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    /// <summary>Copy an incoming photo stream into app storage, downscaling and
    /// re-encoding it to a compact JPEG. Returns the RELATIVE file name to store on the
    /// pet (never an absolute path). The caller owns the source stream's lifetime.</summary>
    public async Task<string> SaveAsync(Stream source)
    {
        var fileName = $"pet_{Guid.NewGuid():N}.jpg";
        await WriteAsync(source, Path.Combine(PhotosDirectory, fileName), MaxEdgePixels);
        return fileName;
    }

    /// <summary>
    /// Stage an incoming photo for the editor: the same decode → orient → downscale as
    /// <see cref="SaveAsync"/>, at editing resolution, into the cache folder. Returns the
    /// staged file with its pixel size, which is what the crop window is expressed
    /// against.
    ///
    /// <para>Note this still BAKES the orientation into the pixels rather than writing an
    /// EXIF tag. Everything downstream depends on that: the vet report embeds this same
    /// file through MigraDoc's <c>AddImage</c>, which does not apply EXIF, so a tag-only
    /// photo would look right in the avatar and sideways in the PDF.</para>
    /// </summary>
    public async Task<PhotoEditSource> PrepareForEditAsync(Stream source)
    {
        var path = Path.Combine(EditsDirectory, $"edit_{Guid.NewGuid():N}.jpg");
        await WriteAsync(source, path, EditEdgePixels);

        var (width, height) = ReadPixelSize(path);
        return new PhotoEditSource(path, width, height, IsTemporary: true);
    }

    /// <summary>Describe a photo that is already in app storage so it can be re-cropped,
    /// the "fix a bad photo without picking it again" path. Not temporary: this file is a
    /// pet's actual photo, and only the draft bookkeeping may delete it.</summary>
    public PhotoEditSource Describe(string fullPath)
    {
        var (width, height) = ReadPixelSize(fullPath);
        return new PhotoEditSource(fullPath, width, height, IsTemporary: false);
    }

    /// <summary>
    /// Apply an editor transform and write the result as the pet's photo. Returns the
    /// RELATIVE file name, exactly like <see cref="SaveAsync"/>, so callers treat an
    /// edited photo and a plain saved one identically.
    ///
    /// <para>A NEW file name every time, never an overwrite: MAUI caches an
    /// <c>ImageSource.FromFile</c> by path, so re-cropping in place would leave every
    /// avatar on screen showing the previous crop until the app restarted. The old file
    /// is disposed of by the caller's draft bookkeeping, which is the half that knows
    /// whether it was a throwaway or a saved pet's photo.</para>
    /// </summary>
    public async Task<string> ApplyAsync(PhotoEditSource source, PhotoTransform transform)
    {
        var fileName = $"pet_{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(PhotosDirectory, fileName);

        // Off the UI thread: this decodes a ~1600px bitmap and re-encodes a JPEG.
        await Task.Run(() => Crop(source, transform, fullPath));
        return fileName;
    }

    /// <summary>Drop a staged edit copy. A no-op for anything that isn't temporary: a
    /// pet's real photo reaches here whenever a re-crop is cancelled, and deleting it
    /// would take the avatar with it.</summary>
    public void DiscardEditSource(PhotoEditSource? source)
    {
        if (source is null || !source.IsTemporary)
            return;

        try
        {
            if (File.Exists(source.Path))
                File.Delete(source.Path);
        }
        catch (Exception ex)
        {
            // Cache; the OS reclaims it eventually. Never worth failing a save over.
            Debug.WriteLine($"[PetPhoto] discard staged edit failed: {ex.Message}");
        }
    }

    /// <summary>Decode → orient → downscale → JPEG at <paramref name="maxEdge"/>, with the
    /// verbatim-copy fallback. Shared by the save and the stage-for-editing paths so a
    /// photo the editor can't process is still a photo the owner keeps.</summary>
    private static async Task WriteAsync(Stream source, string fullPath, int maxEdge)
    {
        try
        {
            // Runs off the UI thread: decoding a large photo is heavy.
            await Task.Run(() => Encode(source, fullPath, maxEdge));
        }
        catch (Exception ex)
        {
            // If decoding/re-encoding fails (unsupported format, corrupt file), fall
            // back to copying the bytes verbatim so the user still gets their photo.
            Debug.WriteLine($"[PetPhoto] downscale failed, copying raw: {ex.Message}");
            if (source.CanSeek)
                source.Seek(0, SeekOrigin.Begin);
            using var outStream = File.Create(fullPath);
            await source.CopyToAsync(outStream);
        }
    }

    /// <summary>
    /// Rotate + crop into a square avatar, on SkiaSharp so there is ONE implementation for
    /// every platform. Safe to be platform-neutral here where <see cref="Encode"/> is not,
    /// because the input is a file this class already wrote: upright, EXIF-free, and
    /// decoded by dimensions we were handed rather than ones we guess.
    ///
    /// <para>The transform chain below is deliberately the same sequence, in the same
    /// order, as the editor's preview drawable, with the crop square's side as the only
    /// difference (output pixels here, screen units there). That is what makes the written
    /// JPEG match what the owner was looking at.</para>
    /// </summary>
    private static void Crop(PhotoEditSource source, PhotoTransform transform, string fullPath)
    {
        // Clamp again rather than trusting the caller: this is the last point before
        // pixels, and an out-of-range crop here would write empty corners into a photo.
        var t = PhotoCrop.Clamp(source.Width, source.Height, transform);

        // SKImage rather than SKBitmap: only the image draw call takes SKSamplingOptions,
        // and the sampling matters: a crop is nearly always a downscale, and Skia's
        // default (no filtering) is visibly gritty on a face.
        using var photo = SKImage.FromEncodedData(source.Path)
            ?? throw new InvalidOperationException($"[PetPhoto] could not decode '{source.Path}'.");

        const float side = MaxEdgePixels;
        using var surface = SKSurface.Create(
            new SKImageInfo(MaxEdgePixels, MaxEdgePixels, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // JPEG carries no alpha, so an unpainted pixel would encode as black. The clamp
        // above means the crop is always full of photo; this is the belt to its braces.
        canvas.Clear(SKColors.White);

        canvas.Translate(side / 2f + t.OffsetX * side, side / 2f + t.OffsetY * side);
        canvas.RotateDegrees(90f * t.QuarterTurns);
        canvas.Scale(PhotoCrop.Scale(source.Width, source.Height, t, side));

        // Positioned from the SOURCE size, not the decoded image's own: the same numbers
        // the clamp and the preview used, so all three agree by construction instead of by
        // two decoders happening to report the same thing.
        canvas.DrawImage(photo, -source.Width / 2f, -source.Height / 2f, CropSampling);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, (int)(JpegQuality * 100));
        using var outStream = File.Create(fullPath);
        data.SaveTo(outStream);
    }

    /// <summary>A photo's pixel size without decoding it. Reads the header only, through
    /// SkiaSharp's codec, which is the same decoder <see cref="Crop"/> will use, so the
    /// numbers the crop window is expressed against are the ones that will actually be
    /// there.</summary>
    private static (int Width, int Height) ReadPixelSize(string fullPath)
    {
        using var codec = SKCodec.Create(fullPath)
            ?? throw new InvalidOperationException($"[PetPhoto] could not read '{fullPath}'.");

        var info = codec.Info;
        if (info.Width <= 0 || info.Height <= 0)
            throw new InvalidOperationException($"[PetPhoto] '{fullPath}' reports no size.");

        return (info.Width, info.Height);
    }

#if ANDROID
    /// <summary>
    /// Decode → orient → downscale → JPEG, on Android's own imaging stack.
    ///
    /// The reason this is platform-specific: a phone held upright captures in the
    /// sensor's landscape frame and records "rotate me" in the JPEG's EXIF
    /// <c>Orientation</c> tag. Android's <c>BitmapFactory</c> (which is what
    /// <c>PlatformImage.FromStream</c> uses underneath) hands back those raw sensor
    /// pixels and never applies the tag, so every camera photo came out a quarter-turn
    /// off, and re-encoding dropped the tag, baking the rotation in permanently.
    /// iOS carries orientation on <c>UIImage</c> and applies it on draw, so it uses the
    /// cross-platform path below unchanged.
    /// </summary>
    private static void Encode(Stream source, string fullPath, int maxEdge)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();

        try
        {
            EncodeUpright(bytes, buffer, fullPath, maxEdge);
        }
        catch (Exception ex)
        {
            // We already hold the whole file, so fall back to the bytes we buffered
            // rather than letting SaveAsync's catch re-read a stream this method has
            // already drained: a non-seekable source would leave a 0-byte photo.
            // The owner keeps their picture; it just isn't downscaled or re-oriented.
            Debug.WriteLine($"[PetPhoto] encode failed, writing the original: {ex.Message}");
            File.WriteAllBytes(fullPath, bytes);
        }
    }

    private static void EncodeUpright(byte[] bytes, MemoryStream buffer, string fullPath, int maxEdge)
    {
        buffer.Position = 0;
        var (degrees, mirrored) = ReadExifOrientation(buffer);

        // Subsampled decode: a 12MP photo never lands in memory whole just to become a
        // small avatar. InSampleSize only does powers of two, so this lands at or above
        // the target and the matrix below finishes the job exactly.
        var bounds = new Android.Graphics.BitmapFactory.Options { InJustDecodeBounds = true };
        Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, bounds);
        var options = new Android.Graphics.BitmapFactory.Options
        {
            InSampleSize = SampleSize(bounds.OutWidth, bounds.OutHeight, maxEdge)
        };

        using var decoded = Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options)
            ?? throw new InvalidOperationException("BitmapFactory returned no bitmap.");

        // Paired with the tag log above. A quarter-turn tag (90/270) on a bitmap that is
        // ALREADY taller than wide means the camera app rotated the pixels itself and
        // left the tag behind: rotating again is what puts the photo back on its side.
        // A phone sensor frame is always landscape before rotation, so this is decidable.
        Debug.WriteLine(
            $"[PetPhoto] decoded {decoded.Width}x{decoded.Height}, applying {degrees}° mirror={mirrored}");

        // Orientation and the final downscale ride one matrix, so the pixels are only
        // copied once however much has to happen to them.
        //
        // ROTATE THEN MIRROR, in that order. The two don't commute for the quarter-turn
        // orientations (5 transpose, 7 transverse), and doing it the other way around
        // reflects those across the wrong axis. It makes no difference to 2 and 4, whose
        // rotation is 0° or 180°: those do commute with a horizontal flip.
        var matrix = new Android.Graphics.Matrix();
        if (degrees != 0)
            matrix.PostRotate(degrees);
        if (mirrored)
            matrix.PostScale(-1, 1);

        var longestEdge = Math.Max(decoded.Width, decoded.Height);
        if (longestEdge > maxEdge)
        {
            var scale = (float)maxEdge / longestEdge;
            matrix.PostScale(scale, scale);
        }

        // Identity means nothing to do: reuse the decode rather than copy it, and then
        // take care not to dispose the same bitmap twice.
        var upright = matrix.IsIdentity
            ? decoded
            : Android.Graphics.Bitmap.CreateBitmap(
                decoded, 0, 0, decoded.Width, decoded.Height, matrix, filter: true)
              ?? decoded;
        try
        {
            using var outStream = File.Create(fullPath);
            upright.Compress(
                Android.Graphics.Bitmap.CompressFormat.Jpeg!,
                (int)(JpegQuality * 100),
                outStream);
        }
        finally
        {
            if (!ReferenceEquals(upright, decoded))
                upright.Dispose();
        }
    }

    /// <summary>The EXIF orientation tag as a rotation plus a mirror flag. Values are
    /// the standard EXIF constants (1 = as-shot) rather than the platform enum, so the
    /// mapping reads the same as the spec.
    ///
    /// Uses AndroidX's reader, not the framework's: <c>android.media.ExifInterface</c> is
    /// deprecated and covers fewer format quirks, and a tag it fails to read is a photo
    /// that silently arrives sideways.</summary>
    private static (int Degrees, bool Mirrored) ReadExifOrientation(Stream stream)
    {
        try
        {
            using var exif = new AndroidX.ExifInterface.Media.ExifInterface(stream);
            var tag = exif.GetAttributeInt(
                AndroidX.ExifInterface.Media.ExifInterface.TagOrientation, 1);

            // Deliberately noisy while the "photos still rotate randomly" report is open:
            // this line plus the decoded dimensions below distinguishes "no tag was
            // present" from "the tag was read and the pixels were already upright".
            Debug.WriteLine($"[PetPhoto] EXIF orientation tag = {tag}");

            return tag switch
            {
                2 => (0, true),     // flip horizontal
                3 => (180, false),
                4 => (180, true),   // flip vertical
                5 => (90, true),    // transpose
                6 => (90, false),
                7 => (270, true),   // transverse
                8 => (270, false),
                _ => (0, false),    // 1 = normal, 0/absent = unknown
            };
        }
        catch (Exception ex)
        {
            // A photo with no or malformed EXIF is normal, not an error: treat it as
            // already upright rather than failing the whole save.
            Debug.WriteLine($"[PetPhoto] EXIF read failed, assuming upright: {ex.Message}");
            return (0, false);
        }
    }

    private static int SampleSize(int width, int height, int maxEdge)
    {
        var sample = 1;
        while (maxEdge > 0 && Math.Max(width, height) / (sample * 2) >= maxEdge)
            sample *= 2;
        return sample;
    }
#else
    /// <summary>Decode → downscale → JPEG. Downsize keeps the aspect ratio and only ever
    /// shrinks (never upscales a small image). Orientation needs no handling here: iOS
    /// carries it on <c>UIImage</c> and applies it on draw, and the desktop targets
    /// receive files from the file system rather than a camera sensor.</summary>
    private static void Encode(Stream source, string fullPath, int maxEdge)
    {
        using var original = PlatformImage.FromStream(source);
        using var scaled = original.Downsize(maxEdge, disposeOriginal: false);
        using var outStream = File.Create(fullPath);
        scaled.Save(outStream, ImageFormat.Jpeg, JpegQuality);
    }
#endif

    /// <summary>Delete a pet photo by its relative file name. Silent no-op when the name
    /// is empty or the file is already gone: a missing photo is never an error.</summary>
    public void Delete(string? fileName)
    {
        if (string.IsNullOrEmpty(fileName))
            return;

        try
        {
            var fullPath = Path.Combine(PhotosDirectory, fileName);
            if (File.Exists(fullPath))
                File.Delete(fullPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PetPhoto] delete '{fileName}' failed: {ex.Message}");
        }
    }

    /// <summary>Delete the entire photos folder: used by the app reset, which must
    /// leave nothing behind. Recreated lazily on the next save. The editor's staging
    /// folder goes with it: a copy left there by a reset mid-edit is still a picture of
    /// the owner's pet, and "everything" has to mean everything.</summary>
    public void DeleteAll()
    {
        DeleteFolder(Path.Combine(FileSystem.AppDataDirectory, "PetPhotos"));
        DeleteFolder(Path.Combine(FileSystem.CacheDirectory, "PetPhotoEdits"));
    }

    private static void DeleteFolder(string dir)
    {
        try
        {
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PetPhoto] delete-all '{dir}' failed: {ex.Message}");
        }
    }
}
