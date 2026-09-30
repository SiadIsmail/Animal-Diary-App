using Animal_Diary_App.Data.Services.Reports;
using CoreGraphics;
using Foundation;
using UIKit;

namespace Animal_Diary_App;

/// <summary>
/// iOS preview rasterizer built on Core Graphics' <see cref="CGPDFDocument"/>, the same
/// shape as <c>AndroidPdfPageRasterizer</c> (OS renderer, no extra native library, so
/// nothing here touches the 16 KB-alignment constraint that removed QuestPDF). Renders
/// each PDF page to a white-backed PNG at the requested DPI. Best-effort per page: a page
/// that fails is skipped, never fatal, because <c>ReportLibraryService.PreviewPathsFor</c>
/// already tolerates missing files.
///
/// <para>This replaces <c>NoOpPdfPageRasterizer</c> on iOS. With the no-op, the report
/// still generated and could still be shared or opened externally, but the in-app
/// Documents viewer showed a report with no pages: a paid-tier surface displaying
/// nothing.</para>
///
/// <para><b>Core Graphics rather than PDFKit.</b> PDFKit's <c>PDFPage.GetThumbnail</c> is
/// fewer lines, but it is a UIKit-adjacent API that wants a main-thread context on some
/// versions, and this runs off the UI thread straight after the PDF is written.
/// <c>CGPDFDocument</c> has no such constraint.</para>
///
/// <para><b><see cref="UIGraphicsImageRenderer"/>, not <c>UIGraphics.BeginImageContext</c>.</b>
/// The older context API is the one every PDF-rasterizing sample on the internet uses, and
/// it is UNSUPPORTED from iOS 17: it fails the build here rather than at runtime, because
/// the csproj treats warnings as errors and the analyzer knows this app's 15.0 floor spans
/// the removal. Do not "simplify" back to it.</para>
///
/// <para><b>The flip is not optional.</b> PDF user space has its origin at the BOTTOM left
/// with y increasing upward; image contexts have it at the TOP left with y increasing
/// downward. Drawing a page without translating to the bottom and scaling y by -1 produces
/// an upside-down preview, which is a plausible-looking image rather than an obvious
/// failure, so it survives a casual glance.</para>
/// </summary>
public sealed class IosPdfPageRasterizer : IPdfPageRasterizer
{
    public Task RasterizeAsync(string pdfPath, int pageCount, Func<int, string> pathForPage, int dpi)
    {
        try
        {
            using var document = CGPDFDocument.FromFile(pdfPath);
            if (document is null)
            {
                System.Diagnostics.Debug.WriteLine("[VetReport] rasterize failed: PDF could not be opened.");
                return Task.CompletedTask;
            }

            // CGPDFDocument pages are 1-based, unlike Android's 0-based PdfRenderer.
            var count = Math.Min(pageCount, (int)document.Pages);
            for (var i = 1; i <= count; i++)
            {
                try
                {
                    using var page = document.GetPage(i);
                    if (page is null)
                        continue;

                    // MediaBox is in points (1/72"), the same unit Android reports, so the
                    // DPI scale is identical on both platforms and previews match.
                    var box = page.GetBoxRect(CGPDFBox.Media);
                    var scale = dpi / 72.0;
                    var width = (nfloat)Math.Ceiling(box.Width * scale);
                    var height = (nfloat)Math.Ceiling(box.Height * scale);
                    if (width <= 0 || height <= 0)
                        continue;

                    // Scale 1 so the size above is taken as pixels rather than being
                    // multiplied again by the device's screen scale (a 3x phone would
                    // otherwise write a 9x-area PNG for every page).
                    var format = new UIGraphicsImageRendererFormat
                    {
                        Scale = 1f,
                        Opaque = true,
                    };

                    var renderer = new UIGraphicsImageRenderer(new CGSize(width, height), format);
                    using var png = renderer.CreatePng(ctx =>
                    {
                        var context = ctx.CGContext;

                        // PDF pages render transparent, and an opaque context starts black:
                        // the report is dark text and would be unreadable either way.
                        context.SetFillColor(1f, 1f, 1f, 1f);
                        context.FillRect(new CGRect(0, 0, width, height));

                        // PDF origin is bottom-left, the image context's is top-left.
                        context.TranslateCTM(0, height);
                        context.ScaleCTM((nfloat)scale, (nfloat)(-scale));

                        // Undo any /MediaBox that does not start at the origin, so a page
                        // with an offset box is not drawn partly outside the bitmap.
                        context.TranslateCTM(-box.X, -box.Y);

                        context.DrawPDFPage(page);
                    });

                    // Save(NSUrl,...) rather than Save(string,...): the string overload is
                    // deprecated from iOS 13 and CA1422 fails the build on it.
                    using var url = NSUrl.FromFilename(pathForPage(i));
                    png.Save(url, atomically: true);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[VetReport] page {i} raster failed: {ex.Message}");
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
