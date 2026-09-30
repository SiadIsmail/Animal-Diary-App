namespace Animal_Diary_App.Data.Services.Reports.Document;

/// <summary>
/// Per-render scratch shared by the document builder and its sections. Holds the usable
/// content width (for sizing charts and table columns) and owns the temporary chart PNGs:
/// MigraDoc reads embedded images from disk at render time, so a chart file has to
/// outlive <c>Compose</c> and is deleted only once the PDF is written (Dispose).
/// </summary>
public sealed class ReportContext : IDisposable
{
    private readonly List<string> _tempFiles = new();

    public ReportContext(double contentWidthPt) => ContentWidthPt = contentWidthPt;

    /// <summary>Printable width in points: page width minus the left+right margins.</summary>
    public double ContentWidthPt { get; }

    /// <summary>Persist chart PNG bytes to a temp file and hand back the path for
    /// <c>Section.AddImage</c>. The file is removed on <see cref="Dispose"/>.</summary>
    public string TempImage(byte[] png)
    {
        var path = Path.Combine(Path.GetTempPath(), $"felova_chart_{Guid.NewGuid():N}.png");
        File.WriteAllBytes(path, png);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* temp cleanup is best-effort */ }
        }
        _tempFiles.Clear();
    }
}
