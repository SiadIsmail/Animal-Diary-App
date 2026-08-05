namespace Animal_Diary_App.Data.Services;

using System.Diagnostics;
using Microsoft.Maui.Graphics;
using Microsoft.Maui.Graphics.Platform;

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

    /// <summary>Copy an incoming photo stream into app storage, downscaling and
    /// re-encoding it to a compact JPEG. Returns the RELATIVE file name to store on the
    /// pet (never an absolute path). The caller owns the source stream's lifetime.</summary>
    public async Task<string> SaveAsync(Stream source)
    {
        var fileName = $"pet_{Guid.NewGuid():N}.jpg";
        var fullPath = Path.Combine(PhotosDirectory, fileName);

        try
        {
            // Runs off the UI thread — decoding a large photo is heavy.
            await Task.Run(() => Encode(source, fullPath));
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

        return fileName;
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
    /// off — and re-encoding dropped the tag, baking the rotation in permanently.
    /// iOS carries orientation on <c>UIImage</c> and applies it on draw, so it uses the
    /// cross-platform path below unchanged.
    /// </summary>
    private static void Encode(Stream source, string fullPath)
    {
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        var bytes = buffer.ToArray();

        try
        {
            EncodeUpright(bytes, buffer, fullPath);
        }
        catch (Exception ex)
        {
            // We already hold the whole file, so fall back to the bytes we buffered
            // rather than letting SaveAsync's catch re-read a stream this method has
            // already drained — a non-seekable source would leave a 0-byte photo.
            // The owner keeps their picture; it just isn't downscaled or re-oriented.
            Debug.WriteLine($"[PetPhoto] encode failed, writing the original: {ex.Message}");
            File.WriteAllBytes(fullPath, bytes);
        }
    }

    private static void EncodeUpright(byte[] bytes, MemoryStream buffer, string fullPath)
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
            InSampleSize = SampleSize(bounds.OutWidth, bounds.OutHeight, MaxEdgePixels)
        };

        using var decoded = Android.Graphics.BitmapFactory.DecodeByteArray(bytes, 0, bytes.Length, options)
            ?? throw new InvalidOperationException("BitmapFactory returned no bitmap.");

        // Paired with the tag log above. A quarter-turn tag (90/270) on a bitmap that is
        // ALREADY taller than wide means the camera app rotated the pixels itself and
        // left the tag behind — rotating again is what puts the photo back on its side.
        // A phone sensor frame is always landscape before rotation, so this is decidable.
        Debug.WriteLine(
            $"[PetPhoto] decoded {decoded.Width}x{decoded.Height}, applying {degrees}° mirror={mirrored}");

        // Orientation and the final downscale ride one matrix, so the pixels are only
        // copied once however much has to happen to them.
        //
        // ROTATE THEN MIRROR, in that order. The two don't commute for the quarter-turn
        // orientations (5 transpose, 7 transverse), and doing it the other way around
        // reflects those across the wrong axis. It makes no difference to 2 and 4, whose
        // rotation is 0° or 180° — those do commute with a horizontal flip.
        var matrix = new Android.Graphics.Matrix();
        if (degrees != 0)
            matrix.PostRotate(degrees);
        if (mirrored)
            matrix.PostScale(-1, 1);

        var longestEdge = Math.Max(decoded.Width, decoded.Height);
        if (longestEdge > MaxEdgePixels)
        {
            var scale = (float)MaxEdgePixels / longestEdge;
            matrix.PostScale(scale, scale);
        }

        // Identity means nothing to do — reuse the decode rather than copy it, and then
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
            // A photo with no or malformed EXIF is normal, not an error — treat it as
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
    private static void Encode(Stream source, string fullPath)
    {
        using var original = PlatformImage.FromStream(source);
        using var scaled = original.Downsize(MaxEdgePixels, disposeOriginal: false);
        using var outStream = File.Create(fullPath);
        scaled.Save(outStream, ImageFormat.Jpeg, JpegQuality);
    }
#endif

    /// <summary>Delete a pet photo by its relative file name. Silent no-op when the name
    /// is empty or the file is already gone — a missing photo is never an error.</summary>
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

    /// <summary>Delete the entire photos folder — used by the app reset, which must
    /// leave nothing behind. Recreated lazily on the next save.</summary>
    public void DeleteAll()
    {
        try
        {
            var dir = Path.Combine(FileSystem.AppDataDirectory, "PetPhotos");
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[PetPhoto] delete-all failed: {ex.Message}");
        }
    }
}
