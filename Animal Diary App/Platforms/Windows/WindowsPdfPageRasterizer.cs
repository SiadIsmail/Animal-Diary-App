using Animal_Diary_App.Data.Services.Reports;
using Windows.Data.Pdf;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Animal_Diary_App;

/// <summary>
/// Windows (dev) preview rasterizer on the WinRT <see cref="PdfDocument"/> API, no extra
/// native library. Renders each page to a PNG stream and writes it out. Best-effort.
/// </summary>
public sealed class WindowsPdfPageRasterizer : IPdfPageRasterizer
{
    public async Task RasterizeAsync(string pdfPath, int pageCount, Func<int, string> pathForPage, int dpi)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(pdfPath);
            var doc = await PdfDocument.LoadFromFileAsync(file);

            var count = Math.Min((uint)pageCount, doc.PageCount);
            for (uint i = 0; i < count; i++)
            {
                try
                {
                    using var page = doc.GetPage(i);
                    var options = new PdfPageRenderOptions
                    {
                        // Size is in points (1/72"); scale the raster width to the target DPI.
                        DestinationWidth = (uint)Math.Ceiling(page.Size.Width * dpi / 72.0)
                    };

                    using var memory = new InMemoryRandomAccessStream();
                    await page.RenderToStreamAsync(memory, options);

                    using var output = System.IO.File.Create(pathForPage((int)i + 1));
                    await memory.AsStreamForRead().CopyToAsync(output);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VetReport] page {i + 1} raster failed: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[VetReport] rasterize failed: {ex.Message}");
        }
    }
}
