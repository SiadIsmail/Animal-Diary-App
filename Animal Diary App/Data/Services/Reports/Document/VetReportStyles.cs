namespace Animal_Diary_App.Data.Services.Reports.Document;

/// <summary>
/// Every tunable of the report's look, in one place. Change the document's
/// appearance here — never inside a section. All lengths are in points (1/72").
///
/// Print rule: the report must survive GRAYSCALE printing, so only black and
/// greys appear here — meaning is never carried by colour.
/// </summary>
public static class VetReportStyles
{
    // ── Page ────────────────────────────────────────────────────────────────
    public const float PageMargin = 28f;
    public const float SectionSpacing = 14f;

    // ── Type ────────────────────────────────────────────────────────────────
    /// <summary>Resolved by Skia per platform: Arial on Windows; Android has no
    /// Arial and silently falls back to the system sans (Roboto). Both print fine.</summary>
    public const string FontFamily = "Arial";
    public const float TitleSize = 16f;
    public const float SectionTitleSize = 9.5f;
    public const float BodySize = 8.5f;
    public const float SmallSize = 7f;
    public const float LineHeight = 1.25f;

    // ── Colours (greys only — see print rule above) ─────────────────────────
    public const string Ink = "#000000";
    public const string InkSecondary = "#444444";
    public const string InkTertiary = "#777777";
    public const string RuleLine = "#999999";
    public const string TableLine = "#bbbbbb";
    public const string ChartGrid = "#cccccc";

    // ── Tables ──────────────────────────────────────────────────────────────
    public const float CellPaddingX = 5f;
    public const float CellPaddingY = 3f;

    // ── Charts ──────────────────────────────────────────────────────────────
    public const float ChartHeight = 78f;
    public const float ChartSpacing = 8f;
    public const float ChartLineWidth = 1.2f;
    public const float ChartMarkerRadius = 1.8f;
    public const float ChartLabelSize = 6.5f;

    // ── Content caps (keeps the document at 1–2 pages on busy pets) ─────────
    public const int MaxEventRows = 30;
    public const int MaxNotes = 10;

    // ── Formats ─────────────────────────────────────────────────────────────
    /// <summary>Unambiguous day-month-year everywhere in the document.</summary>
    public const string DateFormat = "dd MMM yyyy";
    public const string ShortDateFormat = "dd MMM";
    public const string TimeFormat = @"hh\:mm";
}
