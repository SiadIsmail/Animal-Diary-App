namespace Animal_Diary_App.Data.Services.Reports.Document;

using PdfSharp.Fonts;

/// <summary>
/// Supplies the vet report's single sans family to PDFsharp/MigraDoc. The PDFsharp
/// Core build has no access to system fonts, so it needs a resolver that hands back
/// raw TTF bytes. We reuse the app's already-bundled <b>OpenSans</b> (Regular +
/// Semibold): the report only ever asks for a plain or a slightly-heavier weight, so
/// bold maps to Semibold and italic falls back to regular. Any family name the
/// document references (see <see cref="FamilyName"/>) resolves to these faces.
///
/// <see cref="EnsureRegisteredAsync"/> loads the bytes from the app package and installs
/// the resolver ONCE; it must complete before the first MigraDoc render because
/// <see cref="GetFont"/>/<see cref="ResolveTypeface"/> are synchronous.
/// </summary>
public sealed class ReportFontResolver : IFontResolver
{
    /// <summary>The font family the MigraDoc document is styled with.</summary>
    public const string FamilyName = "OpenSans";

    private const string RegularFace = "OpenSans#regular";
    private const string BoldFace = "OpenSans#bold";

    private static byte[]? _regular;
    private static byte[]? _bold;
    private static bool _registered;
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>Load the bundled TTFs and install this resolver as PDFsharp's global
    /// font resolver, exactly once. Safe to call on every export.</summary>
    public static async Task EnsureRegisteredAsync()
    {
        if (_registered)
            return;

        await Gate.WaitAsync();
        try
        {
            if (_registered)
                return;

            _regular = await ReadPackageAsync("OpenSans-Regular.ttf");
            _bold = await ReadPackageAsync("OpenSans-Semibold.ttf");
            GlobalFontSettings.FontResolver ??= new ReportFontResolver();
            _registered = true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private static async Task<byte[]> ReadPackageAsync(string fileName)
    {
        using var stream = await FileSystem.OpenAppPackageFileAsync(fileName);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer);
        return buffer.ToArray();
    }

    public byte[]? GetFont(string faceName) =>
        faceName == BoldFace ? _bold : _regular;

    // Family name is ignored on purpose: whatever the document asks for resolves to the
    // one bundled sans, so a stray "Arial"/default reference can never fail to render.
    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold ? BoldFace : RegularFace);
}
