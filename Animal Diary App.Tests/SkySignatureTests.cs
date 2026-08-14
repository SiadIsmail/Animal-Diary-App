namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// The decorative fingerprint one pet's sky is drawn from.
///
/// <para>Two things are pinned here and both are promises rather than
/// implementation details. <b>Every band is narrow</b> — the point of per-pet
/// variation is that no owner can end up with a bad sky, only a different one, and
/// "we widened one constant" is exactly how that would stop being true. And
/// <b>nothing decorative may change what the picture means</b>: two pets with
/// identical diaries still get every event at the same moment, wearing the same
/// symbol, at the same size.</para>
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
    public void DifferentPets_GetDifferentSkies()
    {
        var charly = SkySignature.For("Charly", 2019);
        var luna = SkySignature.For("Luna", 2019);
        var otherBella = SkySignature.For("Bella", 2016);
        var bella = SkySignature.For("Bella", 2022);

        Assert.NotEqual(charly, luna);
        Assert.NotEqual(bella, otherBella);
    }

    [Fact]
    public void NoName_GetsTheHandTunedDefault()
    {
        Assert.Equal(SkySignature.Default, SkySignature.For(null));
        Assert.Equal(SkySignature.Default, SkySignature.For("  "));
    }

    [Fact]
    public void DefaultIsNeverTheZeroedStruct()
    {
        // default(SkySignature) has zero wavelengths, and PathY divides by them. The
        // Default property exists precisely so that value can never reach the drawing.
        Assert.NotEqual(default, SkySignature.Default);
        Assert.True(SkySignature.Default.LongWavelength > 0);
        Assert.True(SkySignature.Default.ShortWavelength > 0);
    }

    [Theory]
    [InlineData("Charly")]
    [InlineData("Luna")]
    [InlineData("Bella")]
    [InlineData("Milo")]
    [InlineData("Kiki")]
    [InlineData("Otto")]
    [InlineData("小白")]
    [InlineData("Mr. Bigglesworth")]
    public void EverySkyStaysInsideTheBands(string name)
    {
        var sky = SkySignature.For(name, 2020);

        Assert.InRange(sky.PhaseLong, 0, Math.Tau);
        Assert.InRange(sky.PhaseShort, 0, Math.Tau);
        Assert.InRange(sky.LongWavelength, 43, 64);
        Assert.InRange(sky.ShortWavelength, 13, 22);
        Assert.InRange(sky.LongWeight, 0.72, 0.88);
        Assert.InRange(sky.Amplitude, 0.24, 0.32);
        Assert.Contains(sky.AccentKey, SkySignature.Accents);

        // The two weights are one mix, so they always sum to a whole wave.
        Assert.Equal(1.0, sky.LongWeight + sky.ShortWeight, 9);
    }

    [Theory]
    [InlineData("Charly")]
    [InlineData("Luna")]
    [InlineData("Bella")]
    [InlineData("Milo")]
    [InlineData("Kiki")]
    [InlineData("Otto")]
    public void EveryPathStaysOnTheCanvas(string name)
    {
        // A band is only safe if every value inside it draws a line that fits. The
        // amplitude ceiling plus the fan's reach is what keeps stars off the frame.
        var sky = SkySignature.For(name, 2020);

        for (double x = 0; x < 6000; x += 5)
            Assert.InRange(ConstellationLayout.PathY(x, 400, sky), 0, 400);
    }

    [Fact]
    public void TheAccentsAreTheirOwnColours()
    {
        // Never a Star* token: those say which KIND of thing was recorded, and a whole
        // sky glowing in one of them would put a rose wash behind rose glucose stars.
        var categoryColours = CelestialVisuals.All
            .Select(c => CelestialVisuals.For(c).ColorKey)
            .ToHashSet();

        foreach (var accent in SkySignature.Accents)
            Assert.DoesNotContain(accent, categoryColours);
    }

    [Fact]
    public void DecorationNeverMovesAStar()
    {
        // The guarantee the whole idea rests on: change the room, never the record.
        // Two pets, two skies, identical entries — every event lands at the same x.
        var events = Enumerable.Range(0, 40)
            .Select(i => new CelestialEvent(
                new DateTime(2026, 1, 1).AddHours(i * 7), CelestialCategory.Glucose, "x", string.Empty))
            .ToArray();

        var from = new DateTime(2026, 1, 1);
        var to = new DateTime(2026, 1, 31);

        var charly = ConstellationLayout.Place(events, from, to, 900, 300, SkySignature.For("Charly", 2019));
        var luna = ConstellationLayout.Place(events, from, to, 900, 300, SkySignature.For("Luna", 2021));

        for (int i = 0; i < events.Length; i++)
            Assert.Equal(charly[i].X, luna[i].X, 9);
    }
}
