namespace Animal_Diary_App.Tests;

using Xunit;

/// <summary>
/// One weight, one rendering.
///
/// <para><b>The bug this exists for.</b> Today's meta line printed <c>5.19 kg</c> while
/// the stat card beside it printed <c>5.2 kg</c>: the same weigh-in, two numbers, one
/// screen. In an app people trust with medical numbers that is a trust bug wearing a
/// cosmetic disguise, and nothing in the toolchain notices it: both call sites compile,
/// both look reasonable in isolation, and only a screenshot puts them side by side.</para>
///
/// <para>A SOURCE SCAN rather than a unit test, for the same reason
/// <see cref="PaywallBoundaryTests"/> is one: the property worth guarding is not a return
/// value but the ABSENCE of a second formatter. <c>Common_KgSuffix</c> is the tell: any
/// surface writing a weight has to name the unit, so a file naming that key outside the
/// one helper is a file about to disagree with it.</para>
/// </summary>
public class WeightTextTests
{
    private const string Key = "Common_KgSuffix";

    /// <summary>The one place allowed to name the unit. XAML may still translate the key
    /// directly for a LABEL (a field caption, a chart axis): those are listed here so
    /// adding one is a deliberate act rather than a silent second renderer.</summary>
    private static readonly string[] Allowed =
    {
        Path.Combine("Helpers", "WeightText.cs"),
    };

    [Fact]
    public void Only_the_weight_formatter_names_the_kilogram_unit()
    {
        var app = AppFolder();
        var offenders = new List<string>();

        foreach (var path in Directory.EnumerateFiles(app, "*.*", SearchOption.AllDirectories))
        {
            var extension = Path.GetExtension(path);
            if (extension is not (".cs" or ".xaml"))
                continue;
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var relative = Path.GetRelativePath(app, path);
            if (Allowed.Contains(relative, StringComparer.OrdinalIgnoreCase))
                continue;

            if (File.ReadAllText(path).Contains(Key, StringComparison.Ordinal))
                offenders.Add(relative);
        }

        Assert.True(offenders.Count == 0,
            "A weight is rendered by Helpers/WeightText.cs and nowhere else. A second "
            + "renderer is how Today came to show 5.19 kg beside a card showing 5.2 kg. "
            + "Found:\n  " + string.Join("\n  ", offenders));
    }

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
