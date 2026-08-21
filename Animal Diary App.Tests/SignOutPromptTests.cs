namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Cloud;
using Xunit;

/// <summary>
/// <see cref="SignOutImpact"/> is what the sign-out confirmation is built from, so the
/// "is there anything to warn about" decision is worth pinning: getting it wrong either
/// asks a pointless question or (much worse) removes someone's pets silently.
///
/// <para>The sync engine itself is MAUI/SQLite-bound and unreachable from this assembly;
/// the teardown behaviours are covered as device tests in ACCOUNT_LIFECYCLE_PLAN.md §6.</para>
/// </summary>
public class SignOutImpactTests
{
    [Fact]
    public void Nothing_to_lose_asks_nothing()
    {
        // Signed in but holding no synced pets and no pending writes: a confirm here would
        // be noise.
        var impact = new SignOutImpact(Array.Empty<string>(), 0);
        Assert.False(impact.RemovesAnything);
    }

    [Fact]
    public void Pets_leaving_the_device_must_be_announced()
    {
        var impact = new SignOutImpact(new[] { "Bella" }, 0);
        Assert.True(impact.RemovesAnything);
    }

    [Fact]
    public void Unsynced_work_alone_must_be_announced()
    {
        // No pets belong to this account yet, but there are local writes that exist nowhere
        // else. This is the genuinely unrecoverable case and the whole reason sign-out asks
        // rather than tells.
        var impact = new SignOutImpact(Array.Empty<string>(), 3);
        Assert.True(impact.RemovesAnything);
    }
}
