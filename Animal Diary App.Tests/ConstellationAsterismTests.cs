namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Journal;
using Xunit;

/// <summary>
/// The pet's own figure of stars.
///
/// <para>Two of these are promises rather than implementation details, which is why
/// they are pinned here. <b>The same name always draws the same figure</b> — it is
/// the pet's emblem, it appears in things people share, and a figure that changed
/// between launches or between a phone and a tablet would be worth less than no
/// figure at all. And <b>nothing about the animal's health reaches it</b>: the only
/// inputs are a name and a year.</para>
/// </summary>
public class ConstellationAsterismTests
{
    [Fact]
    public void SameName_AlwaysDrawsTheSameFigure()
    {
        var first = ConstellationAsterism.For("Charly", 2019);
        var second = ConstellationAsterism.For("Charly", 2019);

        Assert.Equal(first.Stars, second.Stars);
        Assert.Equal(first.Lines, second.Lines);
    }

    [Fact]
    public void NameIsCaseAndSpaceInsensitive()
    {
        // One animal, however it was typed — otherwise correcting the capitalisation
        // of a name would silently replace the pet's constellation.
        Assert.Equal(
            ConstellationAsterism.For("Charly", 2019).Stars,
            ConstellationAsterism.For("  charly ", 2019).Stars);
    }

    [Fact]
    public void DifferentNames_DrawDifferentFigures()
    {
        var charly = ConstellationAsterism.For("Charly", 2019);
        var luna = ConstellationAsterism.For("Luna", 2019);

        Assert.NotEqual(charly.Stars, luna.Stars);
    }

    [Fact]
    public void SameName_DifferentBirthYear_DrawsADifferentFigure()
    {
        // Two dogs called Bella in one household are two constellations.
        Assert.NotEqual(
            ConstellationAsterism.For("Bella", 2016).Stars,
            ConstellationAsterism.For("Bella", 2022).Stars);
    }

    [Fact]
    public void NoName_DrawsNothingRatherThanAnEmptyShape()
    {
        Assert.False(ConstellationAsterism.For(null).HasShape);
        Assert.False(ConstellationAsterism.For("   ").HasShape);
        Assert.Empty(ConstellationAsterism.For(string.Empty).Lines);
    }

    [Theory]
    [InlineData("Charly")]
    [InlineData("Luna")]
    [InlineData("Mr. Bigglesworth")]
    [InlineData("Ñoño")]
    [InlineData("小白")]
    [InlineData("X")]
    public void EveryFigureIsAJoinedShapeInsideTheFrame(string name)
    {
        var asterism = ConstellationAsterism.For(name, 2020);

        Assert.InRange(asterism.Stars.Count, 6, 9);

        foreach (var star in asterism.Stars)
        {
            // Inside the frame, so no star is cut in half by the card's rounded corner.
            Assert.InRange(star.X, 0.0, 1.0);
            Assert.InRange(star.Y, 0.0, 1.0);
            Assert.InRange(star.Brightness, 0.0, 1.0);
        }

        // Every star after the first is joined to the walk, so the result is a figure
        // and not a scatter.
        Assert.True(asterism.Lines.Count >= asterism.Stars.Count - 1);

        foreach (var line in asterism.Lines)
        {
            Assert.InRange(line.From, 0, asterism.Stars.Count - 1);
            Assert.InRange(line.To, 0, asterism.Stars.Count - 1);
            Assert.NotEqual(line.From, line.To);
        }
    }

    [Fact]
    public void TheFigureKeepsOutOfTheTimelinesCorridor()
    {
        // The band down the middle belongs to the path and its events. The figure sits
        // behind them, not tangled in them.
        foreach (var name in new[] { "Charly", "Luna", "Bella", "Milo", "Kiki", "Otto" })
        {
            foreach (var star in ConstellationAsterism.For(name, 2020).Stars)
            {
                Assert.True(Math.Abs(star.Y - 0.5) >= 0.149,
                    $"{name} put a star at y={star.Y:0.000}, inside the timeline's corridor");
            }
        }
    }

    [Fact]
    public void StarsAreSpreadOut()
    {
        // A walk that folded back on itself would pile stars on one another and read
        // as a smudge rather than a shape.
        var stars = ConstellationAsterism.For("Charly", 2019).Stars;

        for (int i = 1; i < stars.Count; i++)
        {
            var dx = stars[i].X - stars[i - 1].X;
            var dy = stars[i].Y - stars[i - 1].Y;
            Assert.True(Math.Sqrt(dx * dx + dy * dy) > 0.04,
                $"stars {i - 1} and {i} landed on top of each other");
        }
    }
}
