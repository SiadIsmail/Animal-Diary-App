namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services.Cloud;
using Xunit;

/// <summary>
/// The referrer string comes from Google Play, but its CONTENT comes from whoever built the
/// link — anyone can craft one. These pin down what the parser does with real payloads and
/// with junk, since the alternative is finding out on someone's phone.
/// </summary>
public class InstallReferrerParserTests
{
    [Fact]
    public void CreatorParameter_IsTheCandidate()
        => Assert.Equal("theto", InstallReferrerParser.Parse("creator=theto"));

    [Fact]
    public void CreatorWins_OverUtmSource()
    {
        // One link can carry UTM tags for other tools without the two fighting.
        Assert.Equal("theto", InstallReferrerParser.Parse("utm_source=youtube&creator=theto&utm_medium=video"));
    }

    [Fact]
    public void UtmSource_IsTheFallback()
        => Assert.Equal("theto", InstallReferrerParser.Parse("utm_source=theto&utm_medium=video"));

    [Fact]
    public void OrganicPlayInstall_YieldsGooglePlay_WhichTheServerRejects()
    {
        // Every organic install carries this. The parser deliberately does NOT special-case
        // it: "GOOGLE-PLAY" is simply not a creator code, so the server-side lookup returns
        // null. Encoding the exception here would be a second place to keep in sync.
        Assert.Equal("google-play",
            InstallReferrerParser.Parse("utm_source=google-play&utm_medium=organic"));
    }

    [Fact]
    public void ValuesAreUrlDecoded()
        => Assert.Equal("the to", InstallReferrerParser.Parse("creator=the%20to"));

    [Fact]
    public void FirstCreatorWins_SoAnAppendedOneCannotOverride()
        => Assert.Equal("theto", InstallReferrerParser.Parse("creator=theto&creator=someoneelse"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("nonsense-with-no-equals")]
    [InlineData("creator=")]
    [InlineData("utm_medium=organic")]
    public void NothingUsable_IsNull(string? referrer)
        => Assert.Null(InstallReferrerParser.Parse(referrer));

    [Fact]
    public void OneBadEscape_DoesNotLoseTheRestOfTheReferrer()
    {
        // A malformed escape in a string we did not write must cost that pair, not the whole
        // attribution.
        Assert.Equal("theto", InstallReferrerParser.Parse("utm_medium=%zz&creator=theto"));
    }

    [Fact]
    public void KeysAreCaseInsensitive()
        => Assert.Equal("theto", InstallReferrerParser.Parse("Creator=theto"));
}
