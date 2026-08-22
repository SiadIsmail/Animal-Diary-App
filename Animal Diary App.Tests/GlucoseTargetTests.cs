namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// The target band, and the one rule that makes it safe.
///
/// <para><b>A target is never rendered in a different unit from the readings it sits
/// next to.</b> This is the most dangerous interaction per-entry units introduced, and
/// it is dangerous in one specific direction. A band is stored in the unit it was
/// ENTERED in (<c>Tracker.Unit</c>, quoting whatever the vet said) while readings are
/// stored canonical, so the two genuinely can disagree. If a display resolves to mg/dL
/// and the band is printed as stored, an owner sees readings of 137 against a band
/// reading "4-8": not merely wrong, but wrong in the direction that reads as
/// catastrophically low blood sugar, on a screen someone is looking at because their
/// animal is diabetic.</para>
///
/// <para>The same hazard exists in the comparison behind the timeline's range sentence,
/// and it was a latent bug before mg/dL existed: comparing a canonical reading against a
/// raw stored band was right only because every band in the world was mmol/L.</para>
///
/// <para>Felova still never colours, flags or scores a reading against the band. This is
/// entirely about the band being STATED in a unit that matches the numbers beside it.</para>
/// </summary>
public class GlucoseTargetTests
{
    private static readonly UnitDef Mmol =
        UnitCatalog.Get(UnitFamily.Glucose, UnitCatalog.MmolPerLitre);

    private static readonly UnitDef MgDl =
        UnitCatalog.Get(UnitFamily.Glucose, UnitCatalog.MilligramsPerDecilitre);

    // ── The conversion itself ────────────────────────────────────────────────

    /// <summary>A 4-8 mmol/L band is a 72-144 mg/dL band. Those are the numbers on every
    /// veterinary conversion chart, and they are what an American owner must see.</summary>
    [Fact]
    public void A_band_entered_in_mmol_reads_as_the_right_mg_dl_numbers()
    {
        var band = UnitCatalog.ConvertRange(new TargetRange(4m, 8m), Mmol, MgDl);

        Assert.Equal(72m, UnitCatalog.Display(UnitCatalog.ToCanonicalValue(band.Lo, MgDl), MgDl));
        Assert.Equal(144m, UnitCatalog.Display(UnitCatalog.ToCanonicalValue(band.Hi, MgDl), MgDl));
    }

    /// <summary>And back the other way, for the owner whose vet quotes mg/dL while their
    /// own readings are in mmol/L.</summary>
    [Fact]
    public void A_band_entered_in_mg_dl_reads_as_the_right_mmol_numbers()
    {
        var band = UnitCatalog.ConvertRange(new TargetRange(72m, 144m), MgDl, Mmol);

        Assert.Equal(4m, decimal.Round(band.Lo, 1));
        Assert.Equal(8m, decimal.Round(band.Hi, 1));
    }

    [Fact]
    public void Converting_a_band_into_its_own_unit_changes_nothing()
    {
        var band = new TargetRange(4.2m, 8.7m);
        Assert.Equal(band, UnitCatalog.ConvertRange(band, Mmol, Mmol));
    }

    [Fact]
    public void A_band_survives_a_round_trip_through_the_other_unit()
    {
        var band = new TargetRange(4m, 8m);
        var there = UnitCatalog.ConvertRange(band, Mmol, MgDl);
        var back = UnitCatalog.ConvertRange(there, MgDl, Mmol);

        Assert.Equal(4m, decimal.Round(back.Lo, 6));
        Assert.Equal(8m, decimal.Round(back.Hi, 6));
    }

    /// <summary>A legacy tracker row holds the LABEL "mmol/L" rather than a unit id, from
    /// before ids existed. It must resolve to the canonical unit, which is what those rows
    /// have always meant: anything else silently reinterprets every diabetic pet's band.</summary>
    [Theory]
    [InlineData("mmol/L")]
    [InlineData("")]
    [InlineData(null)]
    public void A_legacy_tracker_unit_reads_as_mmol(string? stored)
    {
        var tracker = new Tracker
        {
            TrackerId = TrackerId.Glucose,
            Unit = stored ?? string.Empty,
            TargetLo = 4m,
            TargetHi = 8m,
        };

        Assert.Equal(Mmol, tracker.TargetUnit);
        Assert.Equal(new TargetRange(4m, 8m),
            UnitCatalog.ConvertRange(tracker.TargetRange!.Value, tracker.TargetUnit!.Value, Mmol));
    }

    [Fact]
    public void A_tracker_that_stored_mg_dl_says_so()
    {
        var tracker = new Tracker
        {
            TrackerId = TrackerId.Glucose,
            Unit = UnitCatalog.MilligramsPerDecilitre,
            TargetLo = 72m,
            TargetHi = 144m,
        };

        Assert.Equal(MgDl, tracker.TargetUnit);
    }

    // ── The comparison behind the range sentence ─────────────────────────────

    /// <summary>
    /// A reading of 7.6 mmol/L sits inside a band the owner typed as 72-144 mg/dL. It is
    /// the same band as 4-8 mmol/L, so the answer must be the same either way.
    /// </summary>
    [Fact]
    public void A_reading_is_compared_against_the_band_in_canonical_space()
    {
        var reading = 7.6m;   // mmol/L, as stored

        var typedInMmol = UnitCatalog.CanonicalRange(new TargetRange(4m, 8m), Mmol);
        var typedInMgDl = UnitCatalog.CanonicalRange(new TargetRange(72m, 144m), MgDl);

        Assert.True(typedInMmol.Contains(reading));
        Assert.True(typedInMgDl.Contains(reading));
    }

    /// <summary>
    /// The bug this prevents, stated as a test so it cannot come back. Comparing the
    /// stored bounds directly against a canonical reading says a perfectly ordinary 7.6
    /// is BELOW a band of 72-144. That sentence would appear under a reading on the
    /// timeline, in an app someone opened because their cat is diabetic.
    /// </summary>
    [Fact]
    public void Comparing_a_reading_against_raw_stored_bounds_is_wrong()
    {
        var reading = 7.6m;
        var storedInMgDl = new TargetRange(72m, 144m);

        Assert.False(storedInMgDl.Contains(reading));                              // the bug
        Assert.True(UnitCatalog.CanonicalRange(storedInMgDl, MgDl).Contains(reading)); // the fix
    }

    /// <summary>The band is only ever stated, never applied: a reading outside it is
    /// still just a reading. This pins the arithmetic, not a verdict, and no surface may
    /// turn the boolean into a colour (AI/design-decisions.md).</summary>
    [Fact]
    public void A_reading_outside_the_band_is_still_recognised_in_either_unit()
    {
        var reading = 22.1m;

        Assert.False(UnitCatalog.CanonicalRange(new TargetRange(4m, 8m), Mmol).Contains(reading));
        Assert.False(UnitCatalog.CanonicalRange(new TargetRange(72m, 144m), MgDl).Contains(reading));
    }

    // ── Nothing else may render a band ───────────────────────────────────────

    /// <summary>
    /// One band renderer, and it takes BOTH units so a call site cannot silently print
    /// the stored numbers. A second renderer is how the band and the readings would come
    /// to be resolved separately, which is the whole failure this class exists for.
    /// </summary>
    [Fact]
    public void Only_the_band_renderer_names_the_band_template()
    {
        var app = AppFolder();
        var allowed = Path.Combine("Helpers", "UnitText.cs");

        var offenders = SourceFiles(app)
            .Where(p => !string.Equals(Path.GetRelativePath(app, p), allowed, StringComparison.OrdinalIgnoreCase))
            .Where(p => File.ReadAllText(p).Contains("Common_TargetBand", StringComparison.Ordinal))
            .Select(p => Path.GetRelativePath(app, p))
            .ToList();

        Assert.True(offenders.Count == 0,
            "A target band is rendered by Helpers/UnitText.cs Band(range, storedIn, showIn) "
            + "and nowhere else, so it can never be printed in a unit other than the one "
            + "the readings beside it use. Found:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// The other half: nobody formats a bound directly. <c>r.Lo.ToString(...)</c> is
    /// exactly how the stored numbers reach a screen unconverted, and it compiles, runs
    /// and looks entirely reasonable in review.
    /// </summary>
    [Fact]
    public void No_surface_formats_a_raw_target_bound()
    {
        var app = AppFolder();
        var allowed = Path.Combine("Helpers", "UnitText.cs");
        var raw = new System.Text.RegularExpressions.Regex(@"\.(Lo|Hi)\s*\.\s*ToString\s*\(");

        var offenders = SourceFiles(app)
            .Where(p => !string.Equals(Path.GetRelativePath(app, p), allowed, StringComparison.OrdinalIgnoreCase))
            .Where(p => raw.IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(app, p))
            .ToList();

        Assert.True(offenders.Count == 0,
            "A target bound is stored in the unit it was ENTERED in, so printing it "
            + "directly states a number in whichever unit the owner happened to use, "
            + "beside readings in another. Convert through UnitText.Band. Found:\n  "
            + string.Join("\n  ", offenders));
    }

    private static IEnumerable<string> SourceFiles(string app) =>
        Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".cs" or ".xaml")
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    private static string AppFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Animal Diary App")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Animal Diary App");
    }
}
