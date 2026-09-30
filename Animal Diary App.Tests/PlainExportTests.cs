namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Reports;

using Xunit;

/// <summary>
/// The free, portable half of the report feature: everything the owner wrote down, in
/// order, on every tier forever.
///
/// <para>What is tested here is <see cref="VetReportData.HasAnyData"/>, because it is the
/// one decision the DTO carries and both of its arms fail <i>silently</i>. Get it wrong
/// one way and a free owner is handed a PDF with a title and no rows; get it wrong the
/// other and their export is refused as empty because the <b>designed</b> sections had
/// nothing in them, which would break the promise that getting your data out is never
/// blocked, in the exact case where someone is trying to use it.</para>
/// </summary>
public class PlainExportTests
{
    private static readonly DateTime From = new(2026, 5, 1);
    private static readonly DateTime To = new(2026, 5, 31);

    private static VetReportData Build(
        ReportStyle style,
        IReadOnlyList<ReportLogLine>? log = null,
        IReadOnlyList<ReportNote>? notes = null) => new()
        {
            Style = style,
            Pet = new ReportPetInfo { Name = "Charly", Species = "Dog" },
            From = From,
            To = To,
            GeneratedAt = To,
            PlainLog = log ?? Array.Empty<ReportLogLine>(),
            Notes = notes ?? Array.Empty<ReportNote>(),
        };

    private static ReportLogLine Line(int day, bool hasTime = true) =>
        new(From.AddDays(day).AddHours(hasTime ? 7 : 0), hasTime, "Weigh-in", "8.2 kg");

    [Fact]
    public void A_plain_export_with_entries_is_produced()
    {
        Assert.True(Build(ReportStyle.Plain, log: new[] { Line(1) }).HasAnyData);
    }

    [Fact]
    public void A_plain_export_with_nothing_written_down_is_refused()
    {
        // No empty documents are ever produced: the same rule the designed report follows.
        Assert.False(Build(ReportStyle.Plain).HasAnyData);
    }

    [Fact]
    public void A_plain_export_ignores_the_designed_sections_entirely()
    {
        // The load-bearing one. If HasAnyData still consulted Notes/Medications/Trends here,
        // a plain export would be refused whenever the designed sections were empty, and
        // the free export exists precisely for people who are not making a designed report.
        var noLogButNotes = Build(
            ReportStyle.Plain,
            notes: new[] { new ReportNote(From, "Ate well today") });

        Assert.False(noLogButNotes.HasAnyData);
    }

    [Fact]
    public void The_designed_report_ignores_the_plain_log_entirely()
    {
        // The mirror image, and it matters for the same reason: a designed report must not
        // be declared non-empty by a log it will never render a single line of.
        var logButNoSections = Build(ReportStyle.Designed, log: new[] { Line(1), Line(2) });

        Assert.False(logButNoSections.HasAnyData);
    }

    [Fact]
    public void The_designed_report_is_still_produced_from_its_own_sections()
    {
        var withNotes = Build(
            ReportStyle.Designed,
            notes: new[] { new ReportNote(From, "Ate well today") });

        Assert.True(withNotes.HasAnyData);
    }

    [Fact]
    public void Designed_is_the_default_style()
    {
        // Every existing construction site (the builder's designed path, the sample data)
        // omits Style. If the default ever flipped, they would silently start producing a
        // plain export with an empty log and be refused as having no data.
        Assert.Equal(ReportStyle.Designed, default(VetReportData_StyleProbe).Style);
    }

    /// <summary>A struct whose only job is to read the enum's default member.</summary>
    private struct VetReportData_StyleProbe
    {
        public ReportStyle Style { get; }
    }

    [Fact]
    public void A_row_recorded_without_a_time_is_marked_as_such()
    {
        // A legacy mood/weight row saved before per-entry times existed has no time, and
        // the export prints the date alone rather than claiming midnight. The flag is what
        // carries that; without it the document invents a moment nobody gave it.
        Assert.False(Line(3, hasTime: false).HasTime);
        Assert.True(Line(3).HasTime);
    }
}
