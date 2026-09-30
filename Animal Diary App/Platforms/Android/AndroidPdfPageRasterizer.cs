using Android.Graphics;
using Android.Graphics.Pdf;
using Android.OS;
using Animal_Diary_App.Data.Services.Reports;

namespace Animal_Diary_App;

/// <summary>
/// Android preview rasterizer built on the OS <see cref="PdfRenderer"/>, no extra
/// native library, so it adds nothing to the 16 KB-alignment surface. Renders each PDF
/// page to a white-backed PNG at the requested DPI. Best-effort per page.
/// </summary>
public sealed class AndroidPdfPageRasterizer : IPdfPageRasterizer
{
    public Task RasterizeAsync(string pdfPath, int pageCount, Func<int, string> pathForPage, int dpi)
    {
        try
        {
            using var file = new Java.IO.File(pdfPath);
            using var descriptor = ParcelFileDescriptor.Open(file, ParcelFileMode.ReadOnly);
            using var renderer = new PdfRenderer(descriptor!);

            var count = Math.Min(pageCount, renderer.PageCount);
            for (var i = 0; i < count; i++)
            {
                // PdfRenderer requires each page be closed before the next opens.
                var page = renderer.OpenPage(i);
                try
                {
                    // Page dimensions are in points (1/72"); scale to the requested DPI.
                    var width = (int)Math.Ceiling(page.Width * dpi / 72.0);
                    var height = (int)Math.Ceiling(page.Height * dpi / 72.0);

                    using var bitmap = Bitmap.CreateBitmap(width, height, Bitmap.Config.Argb8888!)!;
                    bitmap.EraseColor(Android.Graphics.Color.White);   // PDF pages render transparent
                    page.Render(bitmap, null, null, PdfRenderMode.ForDisplay);

                    using var stream = System.IO.File.Create(pathForPage(i + 1));
                    bitmap.Compress(Bitmap.CompressFormat.Png!, 100, stream);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VetReport] page {i + 1} raster failed: {ex.Message}");
                }
                finally
                {
                    page.Close();
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VetReport] rasterize failed: {ex.Message}");
        }

        return Task.CompletedTask;
    }
}
