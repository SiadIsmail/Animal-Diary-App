namespace Animal_Diary_App.Tests;

using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Every <c>{StaticResource X}</c> in every XAML file resolves to a key that exists.
///
/// <para><b>Why this is a test and not a code review.</b> A missing static resource is
/// invisible to the compiler: the build succeeds, and the page throws
/// <c>XamlParseException</c> the instant someone opens it. It is a runtime crash on a
/// screen that may not be on anyone's daily path, produced by an edit somewhere else
/// entirely: deleting a colour token that looked unused is enough. This suite caught
/// exactly that: `SkyText` was removed when the Constellation had no text on its dark
/// card, and putting text back there months later crashed the page on open.</para>
///
/// <para>Scope: keys from the app-wide dictionaries (<c>App.xaml</c> and
/// <c>Resources/Styles/</c>) plus each file's own local keys. That mirrors what MAUI
/// actually resolves, so a key defined in one page cannot satisfy a reference in
/// another.</para>
/// </summary>
public class XamlResourceKeyTests
{
    private static readonly Regex KeyPattern = new(@"x:Key=""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex UsePattern = new(@"\{\s*StaticResource\s+([A-Za-z0-9_]+)", RegexOptions.Compiled);

    [Fact]
    public void EveryStaticResourceReferenceResolves()
    {
        var app = AppFolder();
        var global = new HashSet<string>(StringComparer.Ordinal);

        foreach (var path in Directory.EnumerateFiles(Path.Combine(app, "Resources", "Styles"), "*.xaml"))
            AddKeys(global, File.ReadAllText(path));

        var appXaml = Path.Combine(app, "App.xaml");
        if (File.Exists(appXaml))
            AddKeys(global, File.ReadAllText(appXaml));

        var missing = new List<string>();

        foreach (var path in Directory.EnumerateFiles(app, "*.xaml", SearchOption.AllDirectories))
        {
            // obj/ holds generated copies of the same files; checking them would just
            // report every fault twice.
            if (path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                || path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
                continue;

            var xaml = File.ReadAllText(path);

            var available = new HashSet<string>(global, StringComparer.Ordinal);
            AddKeys(available, xaml);

            foreach (Match use in UsePattern.Matches(xaml))
            {
                var key = use.Groups[1].Value;
                if (!available.Contains(key))
                    missing.Add($"{Path.GetFileName(path)} → {key}");
            }
        }

        Assert.True(missing.Count == 0,
            "StaticResource keys that do not exist (each one is a crash when that page "
            + "is opened):" + Environment.NewLine + string.Join(Environment.NewLine, missing.Distinct()));
    }

    private static void AddKeys(HashSet<string> into, string xaml)
    {
        foreach (Match key in KeyPattern.Matches(xaml))
            into.Add(key.Groups[1].Value);
    }

    /// <summary>The app project's folder, found by walking up from the test binary,
    /// the test project deliberately has no reference to it.</summary>
    private static string AppFolder()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Animal Diary App");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "App.xaml")))
                return candidate;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("could not locate the app project from " + AppContext.BaseDirectory);
    }
}
