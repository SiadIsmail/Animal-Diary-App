namespace Animal_Diary_App.Data.Services.Journal;

using SkiaSharp;

// ─────────────────────────────────────────────────────────────────────────────
//  How the sky leaves the app.
//
//  The captured night card is framed on an ordinary Felova page — the rockpool
//  wash, the washi tape, the serif name — and handed to the OS share sheet as one
//  PNG. It is the same picture the owner was just looking at, wearing the page it
//  was looking at it on, so what lands in a group chat is recognisably this app.
//
//  ── What is deliberately NOT in the picture ──
//  The LEGEND. It is the one part of the screen that names conditions out loud
//  ("Seizure", "Medication"), and a shared image travels further than the person
//  sharing it can see. The sky itself is abstract: symbols and moments, no values,
//  no words for what is wrong with the animal. Someone who wants to explain what
//  they are showing can say it themselves; the app must not say it for them, to an
//  audience it knows nothing about.
//
//  ── Why SkiaSharp and not Microsoft.Maui.Graphics.Skia ──
//  The drawable speaks ICanvas, so the obvious move would be an offscreen
//  Maui.Graphics context. That package pins SkiaSharp, and a SkiaSharp pin is
//  exactly the 16 KB-alignment blocker this repo already paid for once with
//  QuestPDF (AI/known-constraints.md). So the sky arrives here as a CAPTURE of the
//  live view — pixel-identical to what was on screen — and only the frame around it
//  is drawn, with the SkiaSharp the report already uses.
// ─────────────────────────────────────────────────────────────────────────────

public static class ConstellationShare
{
    private const int Width = 1080;
    private const int Pad = 64;
    private const int TitleSize = 52;
    private const int SubtitleSize = 28;
    private const int FooterSize = 30;
    private const int CardRadius = 34;

    // The page's own vertical wash (WaterBackground) and ink, as literals because
    // Skia cannot reach a StaticResource and this file has no MAUI to ask.
    private static readonly SKColor WashTop = SKColor.Parse("#DDF2EC");
    private static readonly SKColor WashMid = SKColor.Parse("#C4E6E3");
    private static readonly SKColor WashBottom = SKColor.Parse("#B6DCD9");
    private static readonly SKColor Ink = SKColor.Parse("#0D3A3C");
    private static readonly SKColor InkSecondary = SKColor.Parse("#3A6A6B");
    private static readonly SKColor InkTertiary = SKColor.Parse("#7CA2A1");
    private static readonly SKColor Washi = SKColor.Parse("#A6BEE2D8");

    private static SKTypeface? _serif;
    private static SKTypeface? _sans;
    private static bool _fontsLoaded;

    /// <summary>
    /// Frame the captured sky and write it to the cache as a PNG.
    /// </summary>
    /// <param name="skyPng">The night card, straight from the view.</param>
    /// <param name="title">"Charly's constellation".</param>
    /// <param name="subtitle">The stretch and how much is in it.</param>
    /// <param name="petName">Only ever used to name the FILE, which is the subject
    /// line some share targets show.</param>
    /// <returns>The path to write, or null if the picture could not be built.</returns>
    public static async Task<string?> CreateAsync(byte[] skyPng, string title, string subtitle, string petName)
    {
        if (skyPng.Length == 0)
            return null;

        var png = await Task.Run(() => Compose(skyPng, title, subtitle));
        if (png is null)
            return null;

        var folder = Path.Combine(FileSystem.CacheDirectory, "Share");
        Directory.CreateDirectory(folder);

        // One picture at a time. These are throwaway copies of something the app can
        // redraw in a moment, so a folder of them is just cache that never gets swept.
        foreach (var stale in Directory.EnumerateFiles(folder))
        {
            try { File.Delete(stale); }
            catch (IOException) { /* still held by a share target; it will go next time */ }
        }

        var path = Path.Combine(folder, $"{SafeName(petName)}-constellation.png");
        await File.WriteAllBytesAsync(path, png);
        return path;
    }

    /// <summary>Hand the picture to the OS share sheet — same shape as
    /// <c>ReportActions.ShareAsync</c>, whose bundled FileProvider is what makes a
    /// private app file shareable on Android with no permission.</summary>
    public static Task ShareAsync(string path, string title) =>
        Share.RequestAsync(new ShareFileRequest
        {
            Title = title,
            File = new ShareFile(path)
        });

    // ── The frame ────────────────────────────────────────────────────────────────

    private static byte[]? Compose(byte[] skyPng, string title, string subtitle)
    {
        using var sky = SKBitmap.Decode(skyPng);
        if (sky is null || sky.Width <= 0 || sky.Height <= 0)
            return null;

        EnsureFonts();

        var cardWidth = Width - Pad * 2;
        var cardHeight = (int)Math.Round(cardWidth * (sky.Height / (double)sky.Width));

        using var titleFont = new SKFont(_serif ?? SKTypeface.Default, TitleSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var subtitleFont = new SKFont(_sans ?? SKTypeface.Default, SubtitleSize) { Edging = SKFontEdging.SubpixelAntialias };
        using var footerFont = new SKFont(_serif ?? SKTypeface.Default, FooterSize) { Edging = SKFontEdging.SubpixelAntialias };

        var titleTop = Pad + TitleSize;
        var subtitleTop = titleTop + 44;
        var cardTop = subtitleTop + 46;
        var footerTop = cardTop + cardHeight + 62;
        var height = footerTop + Pad - 10;

        using var surface = SKSurface.Create(new SKImageInfo(Width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;

        // The page ground.
        using (var wash = new SKPaint { IsAntialias = true })
        {
            wash.Shader = SKShader.CreateLinearGradient(
                new SKPoint(0, 0),
                new SKPoint(0, height),
                new[] { WashTop, WashMid, WashBottom },
                new[] { 0f, 0.52f, 1f },
                SKShaderTileMode.Clamp);
            canvas.DrawRect(0, 0, Width, height, wash);
        }

        using var text = new SKPaint { IsAntialias = true };

        text.Color = Ink;
        canvas.DrawText(title, Width / 2f, titleTop, SKTextAlign.Center, titleFont, text);

        text.Color = InkSecondary;
        canvas.DrawText(subtitle, Width / 2f, subtitleTop, SKTextAlign.Center, subtitleFont, text);

        // The card, with the soft ink shadow the page gives it.
        var card = new SKRect(Pad, cardTop, Pad + cardWidth, cardTop + cardHeight);
        var rounded = new SKRoundRect(card, CardRadius);

        using (var shadow = new SKPaint { Color = Ink.WithAlpha(56), IsAntialias = true })
        {
            shadow.MaskFilter = SKMaskFilter.CreateBlur(SKBlurStyle.Normal, 16);
            canvas.DrawRoundRect(new SKRoundRect(
                new SKRect(card.Left, card.Top + 8, card.Right, card.Bottom + 8), CardRadius), shadow);
        }

        canvas.Save();
        canvas.ClipRoundRect(rounded, antialias: true);
        canvas.DrawBitmap(sky, card);
        canvas.Restore();

        // Taped to the page, exactly as it is on screen.
        Tape(canvas, card.Left + 62, card.Top - 8, 128, 34, -4);
        Tape(canvas, card.Right - 156, card.Bottom - 24, 96, 30, 3.5f);

        text.Color = InkTertiary;
        canvas.DrawText("Felova", Width / 2f, footerTop, SKTextAlign.Center, footerFont, text);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Tape(SKCanvas canvas, float x, float y, float w, float h, float degrees)
    {
        using var paint = new SKPaint { Color = Washi, IsAntialias = true };
        canvas.Save();
        canvas.RotateDegrees(degrees, x + w / 2, y + h / 2);
        canvas.DrawRect(x, y, w, h, paint);
        canvas.Restore();
    }

    /// <summary>Load the app's own faces once. A failure is not fatal — the picture
    /// falls back to the platform default rather than not existing, because a share
    /// that silently does nothing is worse than one in the wrong typeface.</summary>
    private static void EnsureFonts()
    {
        if (_fontsLoaded)
            return;

        _fontsLoaded = true;
        _serif = Load("Fraunces.ttf");
        _sans = Load("PlusJakartaSans-Regular.ttf");

        static SKTypeface? Load(string file)
        {
            try
            {
                using var packaged = FileSystem.OpenAppPackageFileAsync(file).GetAwaiter().GetResult();
                using var buffer = new MemoryStream();
                packaged.CopyTo(buffer);
                buffer.Position = 0;
                return SKTypeface.FromStream(buffer);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Constellation] font '{file}' unavailable: {ex.Message}");
                return null;
            }
        }
    }

    /// <summary>A file name from the pet's name. Their own text, so everything a file
    /// system might choke on comes out.</summary>
    private static string SafeName(string? petName)
    {
        var name = (petName ?? string.Empty).Trim();
        if (name.Length == 0)
            return "felova";

        var clean = new string(name
            .Select(c => char.IsLetterOrDigit(c) ? c : '-')
            .ToArray())
            .Trim('-');

        return clean.Length == 0 ? "felova" : clean;
    }
}
