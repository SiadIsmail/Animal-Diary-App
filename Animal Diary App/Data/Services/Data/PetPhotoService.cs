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
            // Downsize keeps the aspect ratio, only ever shrinking (never upscales a
            // small image). Runs off the UI thread — decoding a large photo is heavy.
            await Task.Run(() =>
            {
                using var original = PlatformImage.FromStream(source);
                using var scaled = original.Downsize(MaxEdgePixels, disposeOriginal: false);
                using var outStream = File.Create(fullPath);
                scaled.Save(outStream, ImageFormat.Jpeg, JpegQuality);
            });
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
