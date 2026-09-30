namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Xunit;

/// <summary>
/// The two things about <see cref="SeizureType"/> that can break silently and cannot be
/// caught by the compiler: its stored numbers and its wire names.
///
/// <para>The numbers are the LOCAL storage. sqlite-net reads <c>[StoreAsText]</c> off the
/// property's declared type, so a nullable enum column is written as an integer whatever
/// the attribute says, which makes reordering the members quietly relabel every seizure
/// already logged.</para>
///
/// <para>The names are the CLOUD storage (SyncTableMaps writes <c>Type.ToString()</c> into
/// <c>seizure_entries.seizure_type</c>). Renaming a member would leave every synced row
/// pointing at a value no client can parse, and the defensive TryParse on the read side
/// means that failure is silent: the type just disappears from the record.</para>
/// </summary>
public class SeizureTypeTests
{
    [Theory]
    [InlineData(SeizureType.Generalized, 1, "Generalized")]
    [InlineData(SeizureType.Focal, 2, "Focal")]
    [InlineData(SeizureType.FocalToGeneralized, 3, "FocalToGeneralized")]
    public void StoredValueAndWireNameArePinned(SeizureType type, int stored, string wire)
    {
        Assert.Equal(stored, (int)type);
        Assert.Equal(wire, type.ToString());
    }

    [Fact]
    public void EveryMemberRoundTripsThroughItsWireName()
    {
        foreach (var type in Enum.GetValues<SeizureType>())
        {
            Assert.True(Enum.TryParse<SeizureType>(type.ToString(), out var parsed));
            Assert.Equal(type, parsed);
        }
    }

    [Fact]
    public void UnknownWireValueReadsAsNotSaid()
    {
        // What a newer client's fourth type, or a hand-edited row, must degrade to: the
        // pull drops the type rather than throwing and aborting the whole batch.
        Assert.False(Enum.TryParse<SeizureType>("Myoclonic", out _));
        Assert.False(Enum.TryParse<SeizureType>(null, out _));
    }

    [Fact]
    public void ZeroIsNotAMember()
    {
        // Null is the only "didn't say", so no member may occupy the default(int) slot,
        // otherwise a row that failed to write a value would read back as a real answer.
        Assert.False(Enum.IsDefined((SeizureType)0));
    }
}
