namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// The one decorative thing a pet's sky carries: the colour it leans towards.
///
/// <para>It used to carry a wave shape and a starfield seed as well. Those existed to
/// give a meaningless y axis something to be, and they went when the axis got a
/// meaning, so what is pinned here is only what survived, plus the rule that kept it
/// safe: an ambient hue may never be one of the colours that says WHICH KIND of thing
/// was recorded.</para>
/// </summary>
public class SkySignatureTests
{
    [Fact]
    public void SameName_AlwaysDrawsTheSameSky()
    {
        Assert.Equal(SkySignature.For("Charly", 2019), SkySignature.For("Charly", 2019));
        Assert.Equal(SkySignature.For("Charly", 2019), SkySignature.For(" CHARLY ", 2019));
    }

    [Fact]
    public void NoName_GetsTheDefault()
    {
        Assert.Equal(SkySignature.Default, SkySignature.For(null));
        Assert.Equal(SkySignature.Default, SkySignature.For("  "));
    }

    [Fact]
    public void EverySkyPicksARealAccent()
    {
        foreach (var name in new[] { "Charly", "Luna", "Bella", "Milo", "Kiki", "Otto", "小白" })
            Assert.Contains(SkySignature.For(name, 2020).AccentKey, SkySignature.Accents);
    }

    [Fact]
    public void TheAccentsAreNeverACategoryColour()
    {
        // A whole sky glowing in a category's colour would put a rose wash behind rose
        // glucose stars, and the legend would stop being a promise.
        var categoryColours = CelestialVisuals.All
            .Select(c => CelestialVisuals.For(c).ColorKey)
            .ToHashSet();

        foreach (var accent in SkySignature.Accents)
            Assert.DoesNotContain(accent, categoryColours);
    }

    [Fact]
    public void TheOpeningFocusFollowsTheConditions()
    {
        // Volume is not importance: 180 doses against six seizures. The default is
        // derived, never written, and one tap replaces it.
        Assert.Equal(CelestialCategory.Seizure, CelestialVisuals.OpeningFocusFor(new[] { "epilepsy" }));
        Assert.Equal(CelestialCategory.Glucose, CelestialVisuals.OpeningFocusFor(new[] { "diabetes" }));
        Assert.Equal(CelestialCategory.Water, CelestialVisuals.OpeningFocusFor(new[] { "ckd" }));

        Assert.Null(CelestialVisuals.OpeningFocusFor(null));
        Assert.Null(CelestialVisuals.OpeningFocusFor(Array.Empty<string?>()));
        Assert.Null(CelestialVisuals.OpeningFocusFor(new[] { "", null }));
    }
}
