namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Services;
using Xunit;

/// <summary>
/// The WHERE fragment that keeps seeded demo data off the wire.
///
/// <para>These are string assertions, which is usually a smell — here it is the point. The
/// fragment is interpolated into SQL at three places that can put a row on the wire
/// (<c>TableSync.CollectDirtyAsync</c> and the two bulk sweeps in <c>CloudSyncService</c>),
/// and a wrong fragment does not throw, fail a build, or break a sync: it uploads a
/// creator's invented medical history to their real account, silently and permanently.
/// SQLite is never opened in this project, so the shape of the clause is the part that can
/// be proven here — and it is the part that was reasoned about.</para>
/// </summary>
public class PetScopeSqlTests
{
    // ── Belongs: the pre-existing per-pet fragment, unchanged by the demo work ──

    [Theory]
    [InlineData(PetScope.Root, "Id = ?")]
    [InlineData(PetScope.ByPetId, "PetId = ?")]
    public void Belongs_IsTheDirectColumnTest(PetScope scope, string expected) =>
        Assert.Equal(expected, PetScopeSql.Belongs(scope));

    [Fact]
    public void Belongs_ReachesMedicationRowsThroughTheirMedication()
    {
        // MedicationSchedule has no PetId of its own; it hangs off Medication.
        Assert.Equal(
            "MedicationId in (select Id from \"Medication\" where PetId = ?)",
            PetScopeSql.Belongs(PetScope.ByMedicationId));
    }

    // ── ExcludesDemo: the guard ─────────────────────────────────────────────────

    /// <summary>
    /// The NULL trap, stated as a test.
    ///
    /// <para><c>IsDemo</c> is an additive column, so every pet row written before demo mode
    /// existed holds NULL — and <c>NULL = 0</c> is not true in SQL. Had the root arm been
    /// spelled the obvious way, this clause would have quietly dropped every pre-existing
    /// pet out of the upload queue: the user's own data, silently unsynced, with no error
    /// anywhere. Phrased as set membership, an unknown flag simply fails to join the demo
    /// set and the row syncs.</para>
    /// </summary>
    [Fact]
    public void ExcludesDemo_NeverComparesTheFlagToZero()
    {
        foreach (var scope in Enum.GetValues<PetScope>())
        {
            var sql = PetScopeSql.ExcludesDemo(scope);
            Assert.DoesNotContain("IsDemo = 0", sql);
            Assert.DoesNotContain("IsDemo is null", sql);
        }
    }

    /// <summary>Every arm is the same shape — "not in the demo pet set" — which is what
    /// makes the NULL reading above uniform instead of a special case on one table.</summary>
    [Fact]
    public void ExcludesDemo_IsAlwaysASetMembershipTestAgainstTheDemoPets()
    {
        foreach (var scope in Enum.GetValues<PetScope>())
        {
            var sql = PetScopeSql.ExcludesDemo(scope);
            Assert.Contains("not in (", sql);
            Assert.Contains(PetScopeSql.DemoPetIds, sql);
        }
    }

    [Theory]
    [InlineData(PetScope.Root, "Id not in (select Id from \"Pet\" where IsDemo = 1)")]
    [InlineData(PetScope.ByPetId, "PetId not in (select Id from \"Pet\" where IsDemo = 1)")]
    public void ExcludesDemo_TestsTheRowsOwnColumn(PetScope scope, string expected) =>
        Assert.Equal(expected, PetScopeSql.ExcludesDemo(scope));

    [Fact]
    public void ExcludesDemo_ReachesMedicationRowsThroughTwoHops()
    {
        // A demo pet's medication schedules are two joins from the flag. Getting this arm
        // wrong leaks the seeded dosing schedule while the doses themselves stay put, which
        // is the kind of half-upload nobody would think to look for.
        Assert.Equal(
            "MedicationId not in (select Id from \"Medication\" " +
            "where PetId in (select Id from \"Pet\" where IsDemo = 1))",
            PetScopeSql.ExcludesDemo(PetScope.ByMedicationId));
    }

    /// <summary>The demo set selects a primary key. If it ever selected a nullable column,
    /// <c>NOT IN</c> would collapse to "no rows at all" the moment one row held NULL — and
    /// the symptom would be the whole upload queue silently emptying.</summary>
    [Fact]
    public void DemoPetIds_SelectsThePrimaryKey()
    {
        Assert.StartsWith("select Id from \"Pet\"", PetScopeSql.DemoPetIds);
    }

    // ── Neither fragment may be parameterised ──────────────────────────────────

    /// <summary>The guard takes no parameter: it is a standing condition, not a question
    /// about one animal. A stray <c>?</c> would bind against whatever the caller passed for
    /// a different clause — the sweeps pass nothing at all.</summary>
    [Fact]
    public void ExcludesDemo_BindsNoParameters()
    {
        foreach (var scope in Enum.GetValues<PetScope>())
            Assert.DoesNotContain("?", PetScopeSql.ExcludesDemo(scope));
    }

    [Fact]
    public void Belongs_BindsExactlyOneParameter()
    {
        foreach (var scope in Enum.GetValues<PetScope>())
            Assert.Equal(1, PetScopeSql.Belongs(scope).Count(c => c == '?'));
    }
}
