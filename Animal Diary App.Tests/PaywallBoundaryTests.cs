namespace Animal_Diary_App.Tests;

using System.Text.RegularExpressions;

using Animal_Diary_App.Data.Services.Billing;

using Xunit;

/// <summary>
/// Where the paywall is, and — much more importantly — where it is not.
///
/// <para><b>Why several of these are source scans rather than ordinary unit tests.</b> The
/// enforcement sites are page code-behind and MAUI ViewModels: they cannot be constructed
/// in a plain net10.0 assembly, and the thing worth guarding is not a return value but the
/// <i>absence</i> of a call. A re-added gate compiles, builds, ships, and then charges
/// someone for the right to write down that their dog had a seizure. Nothing else in the
/// toolchain notices that. Same reasoning as <see cref="XamlResourceKeyTests"/>: a runtime
/// hazard the compiler is structurally unable to see gets a test that reads the source.</para>
/// </summary>
public class PaywallBoundaryTests
{
    /// <summary>Every file on a logging path. Each one held a gate before the boundary was
    /// inverted, which is exactly why each one is listed: this is where it grows back.</summary>
    private static readonly string[] LoggingPathFiles =
    {
        Path.Combine("Data", "View", "CalendarPage.xaml.cs"),
        Path.Combine("Data", "View", "ManagePetPage.xaml.cs"),
        Path.Combine("Data", "ViewModels", "MedicationViewModel.cs"),
        Path.Combine("Data", "ViewModels", "JournalLogViewModel.cs"),
    };

    /// <summary>Anything that can withhold a paid surface. A logging path may not name one.</summary>
    private static readonly string[] GateTokens =
    {
        "IEntitlementService",
        "CanEditActivePet",
        "CanEditPet",
        "HasFullAccess",
        "SubscribeVM",
        "SubscribeSheetViewModel",
    };

    [Fact]
    public void No_logging_path_can_reach_the_paywall()
    {
        var app = AppFolder();
        var offenders = new List<string>();

        foreach (var relative in LoggingPathFiles)
        {
            var path = Path.Combine(app, relative);
            if (!File.Exists(path))
                continue;   // the file was renamed; the other assertions still hold

            foreach (var line in File.ReadAllLines(path))
            {
                // Comments are where this rule is written down, so they must be allowed to
                // name the very things the code may not call.
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith("//") || trimmed.StartsWith("///") || trimmed.StartsWith("*"))
                    continue;

                foreach (var token in GateTokens)
                    if (line.Contains(token, StringComparison.Ordinal))
                        offenders.Add($"{relative} → {token}: {trimmed}");
            }
        }

        Assert.True(offenders.Count == 0,
            "The paywall must never appear in the logging path. Writing things down is free "
            + "forever, on every tier, and a gate here means the first bill arrives for the "
            + "right to keep working at 2am about a sick animal. Found:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void AccessState_has_no_trial_and_no_expiry_member()
    {
        // Free replaced both Trial and TrialExpired. Re-adding either would bring back a
        // clock that takes writing away, and would silently fall through the Settings
        // subtitle's default arm on the way.
        var names = Enum.GetNames<AccessState>();

        Assert.Equal(
            new[] { "Unknown", "Free", "Granted", "Subscribed" }.OrderBy(n => n),
            names.OrderBy(n => n));
    }

    [Fact]
    public void Every_AccessState_is_named_in_the_Settings_subtitle()
    {
        // The hazard this guards is specific and has bitten before: that switch used to end
        // in the trial format, so any state without its own arm was described to the user as
        // a free trial with zero minutes left. The fallback is now harmless, but "every
        // state is named" is the property that keeps it that way.
        var source = File.ReadAllText(
            Path.Combine(AppFolder(), "Data", "ViewModels", "SettingsViewModel.cs"));

        var switchStart = source.IndexOf("SubscriptionRowSubtitle", StringComparison.Ordinal);
        Assert.True(switchStart >= 0, "SubscriptionRowSubtitle not found.");
        var switchBody = source.Substring(switchStart, Math.Min(1600, source.Length - switchStart));

        foreach (var name in Enum.GetNames<AccessState>())
        {
            if (name == nameof(AccessState.Unknown))
                continue;   // deliberately the fallback arm, and its copy says so
            Assert.True(
                switchBody.Contains("AccessState." + name, StringComparison.Ordinal),
                $"AccessState.{name} has no arm in SubscriptionRowSubtitle, so it will be "
                + "described to the user as whatever the fallback says.");
        }
    }

    // ── the pet limit ─────────────────────────────────────────────────────────

    [Fact]
    public void A_free_account_with_no_pets_can_add_one()
    {
        Assert.False(PetLimit.BlocksAnotherPet(hasFullAccess: false, livePetCount: 0));
    }

    [Fact]
    public void A_free_account_with_one_pet_cannot_add_a_second()
    {
        Assert.True(PetLimit.BlocksAnotherPet(hasFullAccess: false, livePetCount: 1));
    }

    [Fact]
    public void A_subscriber_can_always_add_another()
    {
        Assert.False(PetLimit.BlocksAnotherPet(hasFullAccess: true, livePetCount: 1));
        Assert.False(PetLimit.BlocksAnotherPet(hasFullAccess: true, livePetCount: 7));
    }

    [Fact]
    public void A_lapsed_subscriber_keeps_the_pets_they_have_and_is_only_stopped_at_the_door()
    {
        // Three animals, subscription gone. The rule answers ONE question — may a NEW pet be
        // created — and it is the only question it may ever answer. Nothing anywhere hides,
        // locks or read-onlys an animal that already exists to enforce a plan limit.
        Assert.True(PetLimit.BlocksAnotherPet(hasFullAccess: false, livePetCount: 3));
    }

    // ── the pieces that never gate anything ──────────────────────────────────

    [Fact]
    public void The_gate_exposes_nothing_a_logging_path_could_be_tempted_by()
    {
        // No trial clock, no days-left, no "has it started" — the members that used to make
        // a countdown possible are gone from the boundary entirely, so no surface can build
        // one out of them.
        var members = typeof(IEntitlementService).GetMembers().Select(m => m.Name).ToArray();

        Assert.DoesNotContain("TrialDaysLeft", members);
        Assert.DoesNotContain("TrialTimeRemaining", members);
        Assert.DoesNotContain("TrialEverStarted", members);
        Assert.DoesNotContain("EnsureTrialStartedAsync", members);
        // EverGranted survives, and must: it is what stops someone whose redeemed year ran
        // out being addressed as though they had cancelled a subscription.
        Assert.Contains("EverGranted", members);
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
