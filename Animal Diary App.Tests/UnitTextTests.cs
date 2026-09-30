namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// One reading, one rendering.
///
/// <para><b>The bug this exists for.</b> Today's meta line printed <c>5.19 kg</c> while
/// the stat card beside it printed <c>5.2 kg</c>: the same weigh-in, two numbers, one
/// screen. In an app people trust with medical numbers that is a trust bug wearing a
/// cosmetic disguise, and nothing in the toolchain notices it: both call sites compile,
/// both look reasonable in isolation, and only a screenshot puts them side by side.</para>
///
/// <para>This replaced <c>WeightTextTests</c>, which guarded the same property for one
/// record by watching a single resource key (<c>Common_KgSuffix</c>). Units are chosen
/// per entry now, so every measured record has the identical hazard: a surface that
/// names a unit label itself is a surface about to disagree with the formatter, and a
/// surface that prints a bare number is one that cannot be read at all, because 14
/// mmol/L and 14 mg/dL are the same digits and different medicine.</para>
///
/// <para>A SOURCE SCAN rather than a unit test, for the same reason
/// <see cref="PaywallBoundaryTests"/> is one: the property worth guarding is not a
/// return value but the ABSENCE of a second renderer.</para>
/// </summary>
public class UnitTextTests
{
    /// <summary>The unit label keys. Any file naming one of these is choosing how a unit
    /// is written, which is <c>UnitCatalog</c>'s job.</summary>
    private static readonly string[] LabelKeys =
    {
        "Unit_Kg", "Unit_Lb", "Unit_G", "Unit_MmolL", "Unit_MgDl",
        "Unit_Ml", "Unit_FlOz", "Unit_Cup", "Unit_Oz", "Unit_Sec", "Unit_Min",
    };

    /// <summary>The one place allowed to name them: the catalog row that declares each
    /// unit. Adding a file here is a deliberate act, not a merge accident.</summary>
    private static readonly string[] Allowed =
    {
        Path.Combine("Data", "Models", "Units.cs"),
    };

    [Fact]
    public void Only_the_unit_catalog_names_a_unit_label()
    {
        var app = AppFolder();
        var offenders = new List<string>();

        foreach (var path in SourceFiles(app))
        {
            var relative = Path.GetRelativePath(app, path);
            if (Allowed.Contains(relative, StringComparer.OrdinalIgnoreCase))
                continue;

            var text = File.ReadAllText(path);
            foreach (var key in LabelKeys)
                if (text.Contains(key, StringComparison.Ordinal))
                {
                    offenders.Add($"{relative} (names {key})");
                    break;
                }
        }

        Assert.True(offenders.Count == 0,
            "A unit's label is declared once, on its UnitCatalog row, and reached through "
            + "UnitDef.Label / Helpers.UnitText. A second namer is how Today came to show "
            + "5.19 kg beside a card showing 5.2 kg. Found:\n  "
            + string.Join("\n  ", offenders));
    }

    /// <summary>
    /// <c>WeightText</c> is gone and must stay gone. It was the per-record formatter that
    /// <see cref="Helpers.UnitText"/> generalised; a file resurrecting the name is a
    /// second formatter for one record, which is the exact shape of the original bug.
    /// </summary>
    [Fact]
    public void The_per_record_weight_formatter_is_not_resurrected()
    {
        var app = AppFolder();
        Assert.False(File.Exists(Path.Combine(app, "Helpers", "WeightText.cs")),
            "Helpers/WeightText.cs is retired: weight is one family among five and shares "
            + "Helpers/UnitText.cs with them.");

        // A member ACCESS on the old type, not the bare word: "LatestWeightText" is an
        // honest property name and the prose in UnitText.cs explains what it replaced.
        var call = new System.Text.RegularExpressions.Regex(@"(?<![A-Za-z0-9_])WeightText\s*\.");

        var offenders = SourceFiles(app)
            .Where(p => call.IsMatch(File.ReadAllText(p)))
            .Select(p => Path.GetRelativePath(app, p))
            .ToList();

        Assert.True(offenders.Count == 0,
            "Nothing may call WeightText: weight renders through Helpers/UnitText.cs like "
            + "every other measured record. Found:\n  " + string.Join("\n  ", offenders));
    }

    /// <summary>Every label key the catalog declares actually exists in BOTH resx files.
    /// A missing key does not throw: <c>LocalizationManager</c> returns the key itself, so
    /// a typo ships as a weight of "5.19 Unit_Kg" and only a screenshot catches it.</summary>
    [Fact]
    public void Every_declared_unit_has_a_label_in_both_languages()
    {
        var app = AppFolder();
        var en = File.ReadAllText(Path.Combine(app, "Resources", "Strings", "AppStrings.resx"));
        var de = File.ReadAllText(Path.Combine(app, "Resources", "Strings", "AppStrings.de.resx"));

        foreach (var unit in UnitCatalog.All)
        {
            var declaration = $"name=\"{unit.LabelKey}\"";
            Assert.True(en.Contains(declaration, StringComparison.Ordinal),
                $"{unit.Id} has no {unit.LabelKey} in AppStrings.resx.");
            Assert.True(de.Contains(declaration, StringComparison.Ordinal),
                $"{unit.Id} has no {unit.LabelKey} in AppStrings.de.resx.");
        }
    }

    private static IEnumerable<string> SourceFiles(string app) =>
        Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories)
            .Where(p => Path.GetExtension(p) is ".cs" or ".xaml")
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"));

    /// <summary>The app project folder, found by walking up from the test binaries.</summary>
    private static string AppFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Animal Diary App")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Animal Diary App");
    }
}
