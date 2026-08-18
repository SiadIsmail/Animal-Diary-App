namespace Animal_Diary_App.Tests;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Import;
using Xunit;

/// <summary>
/// The import bridge's rules. These are the tests that matter for this feature: the
/// validator is the only thing standing between a file an AI made up and the owner's
/// medical history, and every rule it enforces is invisible until it fails on real data.
///
/// <para>Grouped by the promise each one keeps rather than by method, because that is how
/// they are reasoned about: "a file never partly lands", "an import never overwrites",
/// "the same notes imported twice do not double up".</para>
/// </summary>
public class ImportValidatorTests
{
    private static readonly DateTime Today = new(2026, 8, 18);

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private static ImportPlan Validate(string json, ImportSnapshot? snapshot = null)
    {
        Assert.True(ImportFileParser.TryParse(json, out var file, out var error), error?.Message);
        return ImportValidator.Validate(file, snapshot ?? ImportSnapshot.Empty, Today);
    }

    /// <summary>A file wrapper, so each test states only the part it is about.</summary>
    private static string File(string petsJson) =>
        $$"""
        { "felova_import_version": 1, "pets": [ {{petsJson}} ] }
        """;

    private static string ExistingPetBlock(string entriesJson, string name = "Charly") =>
        $$"""
        { "match": "existing", "name": "{{name}}", "entries": [ {{entriesJson}} ] }
        """;

    private static ImportSnapshot WithPet(
        int id = 1,
        string name = "Charly",
        string species = "Dog",
        IEnumerable<ExistingTracker>? trackers = null,
        IEnumerable<ExistingPetDay>? days = null,
        IEnumerable<ExistingLevelDay>? levelDays = null,
        IEnumerable<ExistingEvent>? events = null) => new()
        {
            Pets = new[] { new ExistingPet(id, name, species) },
            Trackers = (trackers ?? Array.Empty<ExistingTracker>()).ToList(),
            PetDays = (days ?? Array.Empty<ExistingPetDay>()).ToList(),
            LevelDays = (levelDays ?? Array.Empty<ExistingLevelDay>()).ToList(),
            Events = (events ?? Array.Empty<ExistingEvent>()).ToList(),
        };

    // ── The file must declare a version this build understands ──────────────────

    [Fact]
    public void MissingVersion_RejectsTheFile()
    {
        var plan = Validate("""{ "pets": [] }""");

        Assert.False(plan.IsValid);
        Assert.Contains(ImportFormat.VersionKey, plan.Errors[0].Message);
    }

    [Fact]
    public void FutureVersion_RejectsWithoutReadingAnything()
    {
        // The point: it does NOT go on to complain about the entries. A newer format may
        // mean something different by the same field names, so nothing else is trusted.
        var plan = Validate("""
            { "felova_import_version": 99, "pets": [ { "match": "nonsense" } ] }
            """);

        Assert.False(plan.IsValid);
        Assert.Single(plan.Errors);
    }

    // ── A single error rejects everything ───────────────────────────────────────

    [Fact]
    public void OneBadEntry_RejectsTheWholeFile_AndReportsEveryProblemAtOnce()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "weight", "date": "2026-08-10", "value_kg": 18.4 },
            { "type": "weight", "date": "not-a-date", "value_kg": 18.4 },
            { "type": "glucose", "date": "2026-08-11", "value": 8.2 }
            """)), WithPet());

        Assert.False(plan.IsValid);
        Assert.Empty(plan.Pets);

        // Both problems in one pass — a bad date AND the glucose reading missing its
        // food context — so the owner fixes the file once rather than three times.
        Assert.Equal(2, plan.Errors.Count);
        Assert.Contains(plan.Errors, e => e.Message.Contains("not a date"));
        Assert.Contains(plan.Errors, e => e.Message.Contains("context"));
    }

    [Fact]
    public void UnknownEntryType_IsAnError_NotASilentSkip()
    {
        // Dropping it quietly would let the file claim to have imported something it did
        // not — the owner would believe the vomiting was recorded.
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "vomiting", "date": "2026-08-10" }
            """)), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("Unknown entry type", plan.Errors[0].Message);
    }

    [Fact]
    public void UnknownField_IsANotice_NotAnError()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "weight", "date": "2026-08-10", "value_kg": 18.4, "measured_by": "vet" }
            """)), WithPet());

        Assert.True(plan.IsValid);
        Assert.Contains(plan.AllNotices, n => n.Kind == ImportNoticeKind.UnknownField && n.Message.Contains("measured_by"));
    }

    // ── Dates ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("2026-09-01")]  // beyond the one-day grace
    [InlineData("1924-08-10")]  // a mis-transcribed year
    public void ImplausibleDate_RejectsTheFile(string date)
    {
        var plan = Validate(File(ExistingPetBlock(
            $$"""{ "type": "weight", "date": "{{date}}", "value_kg": 18.4 }""")), WithPet());

        Assert.False(plan.IsValid);
    }

    [Fact]
    public void TomorrowIsAccepted_BecauseTheFileMayComeFromAnotherTimezone()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "weight", "date": "2026-08-19", "value_kg": 18.4 }
            """)), WithPet());

        Assert.True(plan.IsValid);
    }

    // ── Mood and weight share one row per day ───────────────────────────────────

    [Fact]
    public void MoodAndWeightOnTheSameDay_BecomeOneRow()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "mood", "date": "2026-08-11", "level": 4 },
            { "type": "weight", "date": "2026-08-11", "value_kg": 18.4 }
            """)), WithPet());

        Assert.True(plan.IsValid);
        var day = Assert.Single(plan.Pets[0].Days);
        Assert.True(day.HasMood);
        Assert.True(day.HasWeight);

        // Two entries as far as the owner is concerned, even though they share a row.
        Assert.Equal(2, plan.Pets[0].EntryCount);
    }

    [Fact]
    public void TwoMoodsForOneDay_ContradictEachOther_AndReject()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "mood", "date": "2026-08-11", "level": 4 },
            { "type": "mood", "date": "2026-08-11", "level": 2 }
            """)), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("second mood", plan.Errors[0].Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── An import never overwrites what the owner recorded ──────────────────────

    [Fact]
    public void ADayThatAlreadyHasAWeight_SkipsTheWeightButStillFillsTheMood()
    {
        var day = new DateTime(2026, 8, 11);
        var snapshot = WithPet(days: new[]
        {
            new ExistingPetDay(1, day, RowId: 77, HasMood: false, HasWeight: true, IsTombstone: false),
        });

        var plan = Validate(File(ExistingPetBlock("""
            { "type": "mood", "date": "2026-08-11", "level": 4 },
            { "type": "weight", "date": "2026-08-11", "value_kg": 18.4 }
            """)), snapshot);

        Assert.True(plan.IsValid);

        var planned = Assert.Single(plan.Pets[0].Days);
        Assert.True(planned.HasMood);
        Assert.False(planned.HasWeight);

        // It merges into the row that is already there rather than inserting a sibling.
        Assert.Equal(77, planned.RowId);

        Assert.Equal(1, plan.TotalSkippedCount);
        Assert.Contains(plan.AllNotices, n => n.Kind == ImportNoticeKind.SlotOccupied);
    }

    [Fact]
    public void ASoftDeletedDay_IsRevivedRatherThanDuplicated()
    {
        // The cloud keys this table by (pet, day), so a second row would collapse onto
        // the first on the next pull.
        var day = new DateTime(2026, 8, 11);
        var snapshot = WithPet(days: new[]
        {
            new ExistingPetDay(1, day, RowId: 77, HasMood: true, HasWeight: false, IsTombstone: true),
        });

        var plan = Validate(File(ExistingPetBlock("""
            { "type": "mood", "date": "2026-08-11", "level": 4 }
            """)), snapshot);

        Assert.True(plan.IsValid);
        var planned = Assert.Single(plan.Pets[0].Days);
        Assert.Equal(77, planned.RowId);
        Assert.True(planned.ReviveTombstone);
        Assert.True(planned.HasMood);
    }

    [Fact]
    public void AnOccupiedAppetiteDay_IsSkipped_ButATombstonedOneIsRevived()
    {
        var snapshot = WithPet(levelDays: new[]
        {
            new ExistingLevelDay(1, ImportEntryType.AppetiteLevel, new DateTime(2026, 8, 11), RowId: 5, IsTombstone: false),
            new ExistingLevelDay(1, ImportEntryType.AppetiteLevel, new DateTime(2026, 8, 12), RowId: 6, IsTombstone: true),
        });

        var plan = Validate(File(ExistingPetBlock("""
            { "type": "appetite_level", "date": "2026-08-11", "level": 3 },
            { "type": "appetite_level", "date": "2026-08-12", "level": 4 }
            """)), snapshot);

        Assert.True(plan.IsValid);
        var written = Assert.Single(plan.Pets[0].AppetiteLevels);
        Assert.Equal(6, written.UpdateRowId);
        Assert.True(written.IsRevival);
        Assert.Equal(1, plan.TotalSkippedCount);
    }

    // ── The same notes imported twice do not double up ──────────────────────────

    [Fact]
    public void AnEventAlreadyStored_IsSkipped()
    {
        var snapshot = WithPet(events: new[]
        {
            new ExistingEvent(1, ImportEntryType.Seizure, new DateTime(2026, 8, 10), new TimeSpan(20, 0, 0), 1m),
        });

        var plan = Validate(File(ExistingPetBlock("""
            { "type": "seizure", "date": "2026-08-10", "time": "20:00", "duration_minutes": 1 },
            { "type": "seizure", "date": "2026-08-14", "time": "06:15", "duration_minutes": 2 }
            """)), snapshot);

        Assert.True(plan.IsValid);
        Assert.Single(plan.Pets[0].Seizures);
        Assert.Equal(1, plan.TotalSkippedCount);
        Assert.Contains(plan.AllNotices, n => n.Kind == ImportNoticeKind.AlreadyPresent);
    }

    // ── Missing times stay missing ──────────────────────────────────────────────

    [Fact]
    public void AnOmittedTime_IsNullOnMood_AndStartOfDayOnAnEvent()
    {
        // PetEntry's time columns are nullable and null already means "unknown" app-wide,
        // so a missing time is stored honestly rather than as midnight. Event tables have
        // no nullable time, so they use the same start-of-day legacy rows already use.
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "mood", "date": "2026-08-11", "level": 4 },
            { "type": "seizure", "date": "2026-08-12" }
            """)), WithPet());

        Assert.True(plan.IsValid);
        Assert.Null(plan.Pets[0].Days[0].MoodTimeTicks);
        Assert.Equal(TimeSpan.Zero, plan.Pets[0].Seizures[0].Time);
    }

    // ── Matching a pet ──────────────────────────────────────────────────────────

    [Fact]
    public void NoMatchingPet_SaysSo_AndNamesWhatIsOnTheDevice()
    {
        var plan = Validate(File(ExistingPetBlock("", name: "Rex")), WithPet(name: "Charly"));

        Assert.False(plan.IsValid);
        Assert.Contains("Charly", plan.Errors[0].Message);
    }

    [Fact]
    public void TwoPetsWithTheSameName_IsAmbiguous_AndRejects()
    {
        var snapshot = new ImportSnapshot
        {
            Pets = new[] { new ExistingPet(1, "Max", "Dog"), new ExistingPet(2, "Max", "Cat") },
        };

        var plan = Validate(File(ExistingPetBlock("", name: "Max")), snapshot);

        Assert.False(plan.IsValid);
        Assert.Contains("matches 2 pets", plan.Errors[0].Message);
    }

    [Fact]
    public void SpeciesBreaksATie()
    {
        var snapshot = new ImportSnapshot
        {
            Pets = new[] { new ExistingPet(1, "Max", "Dog"), new ExistingPet(2, "Max", "Cat") },
        };

        var plan = Validate(File("""
            { "match": "existing", "name": "Max", "species": "cat",
              "entries": [ { "type": "weight", "date": "2026-08-10", "value_kg": 4.2 } ] }
            """), snapshot);

        Assert.True(plan.IsValid);
        Assert.Equal(2, plan.Pets[0].ExistingPetId);
    }

    [Fact]
    public void TwoBlocksAimingAtOnePet_Reject()
    {
        // Each block resolves collisions against the snapshot and cannot see what the
        // other is about to write, so this would slip a duplicate past both checks.
        var plan = Validate("""
            { "felova_import_version": 1, "pets": [
              { "match": "existing", "name": "Charly", "entries": [] },
              { "match": "existing", "name": "Charly", "entries": [] }
            ] }
            """, WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("already the target", plan.Errors[0].Message);
    }

    // ── Creating a pet ──────────────────────────────────────────────────────────

    [Fact]
    public void ANewPet_NeedsASpeciesAndABirthYear()
    {
        var plan = Validate(File("""
            { "match": "new", "name": "Charly", "entries": [] }
            """));

        Assert.False(plan.IsValid);
        Assert.Contains(plan.Errors, e => e.Message.Contains("species"));
        Assert.Contains(plan.Errors, e => e.Message.Contains("birth_year"));
    }

    [Fact]
    public void ANewPet_KeepsAnUnknownMonthAndDayNull()
    {
        var plan = Validate(File("""
            { "match": "new", "name": "Charly", "species": "Dog", "birth_year": 2019,
              "conditions": ["epilepsy"], "entries": [] }
            """));

        Assert.True(plan.IsValid);
        var pet = plan.Pets[0].NewPet!;
        Assert.Equal(2019, pet.BirthYear);
        Assert.Null(pet.BirthMonth);
        Assert.Null(pet.BirthDay);
        Assert.Equal(new[] { "epilepsy" }, pet.ConditionIds);
    }

    [Fact]
    public void ANewPetWithAnExistingName_IsAllowedButFlagged()
    {
        // People do reuse names, so this is not an error — but it is also exactly what a
        // wrongly-chosen "new" looks like, so the owner is told before confirming.
        var plan = Validate(File("""
            { "match": "new", "name": "Charly", "species": "Dog", "birth_year": 2019, "entries": [] }
            """), WithPet(name: "Charly"));

        Assert.True(plan.IsValid);
        Assert.Contains(plan.AllNotices, n => n.Message.Contains("already exists"));
    }

    [Fact]
    public void AnUnknownCondition_Rejects()
    {
        var plan = Validate(File("""
            { "match": "new", "name": "Charly", "species": "Dog", "birth_year": 2019,
              "conditions": ["arthritis"], "entries": [] }
            """));

        Assert.False(plan.IsValid);
        Assert.Contains("Unknown condition", plan.Errors[0].Message);
    }

    // ── Custom trackers ─────────────────────────────────────────────────────────

    [Fact]
    public void ANewCustomTracker_IsCreatedAndItsEntriesPointAtIt()
    {
        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [
                { "ref": "vomit", "name": "Vomiting", "shape": "tick", "icon": "🤢", "color": "rose" }
              ],
              "entries": [
                { "type": "custom", "tracker": "vomit", "date": "2026-08-11", "time": "06:30", "note": "Undigested food" }
              ] }
            """), WithPet());

        Assert.True(plan.IsValid);

        var tracker = Assert.Single(plan.Pets[0].Trackers);
        Assert.True(tracker.IsNew);
        Assert.Equal("Vomiting", tracker.Definition!.Name);
        Assert.Equal(CustomShape.Tick, tracker.Definition.Shape);

        // A cadence nobody stated must never nag.
        Assert.Equal(TrackerKind.Event, tracker.Definition.Kind);

        var entry = Assert.Single(plan.Pets[0].CustomEntries);
        Assert.Equal("vomit", entry.TrackerRef);
        Assert.Null(entry.Row.Amount);
    }

    [Fact]
    public void AnExistingTrackerIsReusedByName_NotDuplicated()
    {
        var snapshot = WithPet(trackers: new[]
        {
            new ExistingTracker(9, 1, "Walk", CustomShape.Amount, IsArchived: false),
        });

        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "w", "name": "walk", "shape": "amount", "unit": "min" } ],
              "entries": [ { "type": "custom", "tracker": "w", "date": "2026-08-11", "amount": 35 } ] }
            """), snapshot);

        Assert.True(plan.IsValid);
        var tracker = Assert.Single(plan.Pets[0].Trackers);
        Assert.False(tracker.IsNew);
        Assert.Equal(9, tracker.ExistingId);
        Assert.Null(tracker.Definition);
    }

    [Fact]
    public void AnArchivedTrackerIsReused_AndTheOwnerIsToldItStaysRetired()
    {
        var snapshot = WithPet(trackers: new[]
        {
            new ExistingTracker(9, 1, "Walk", CustomShape.Tick, IsArchived: true),
        });

        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "w", "name": "Walk", "shape": "tick" } ],
              "entries": [ { "type": "custom", "tracker": "w", "date": "2026-08-11" } ] }
            """), snapshot);

        Assert.True(plan.IsValid);
        Assert.Equal(9, plan.Pets[0].Trackers[0].ExistingId);
        Assert.True(plan.Pets[0].Trackers[0].MatchedArchived);
        Assert.Contains(plan.AllNotices, n => n.Message.Contains("retired"));
    }

    [Fact]
    public void ATrackerWhoseShapeDisagreesWithTheDevice_Rejects()
    {
        // Writing amounts into a tracker the owner created as a tick produces entries no
        // surface can render as they meant, and nothing downstream would flag it.
        var snapshot = WithPet(trackers: new[]
        {
            new ExistingTracker(9, 1, "Walk", CustomShape.Tick, IsArchived: false),
        });

        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "w", "name": "Walk", "shape": "amount", "unit": "min" } ],
              "entries": [] }
            """), snapshot);

        Assert.False(plan.IsValid);
        Assert.Contains("already exists", plan.Errors[0].Message);
    }

    [Fact]
    public void AnAmountTrackerWithoutAUnit_Rejects()
    {
        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "w", "name": "Walk", "shape": "amount" } ],
              "entries": [] }
            """), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("unit", plan.Errors[0].Message);
    }

    [Fact]
    public void AnAmountOnATickTracker_Rejects()
    {
        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "p", "name": "Poop", "shape": "tick" } ],
              "entries": [ { "type": "custom", "tracker": "p", "date": "2026-08-11", "amount": 2 } ] }
            """), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("tick tracker", plan.Errors[0].Message);
    }

    [Fact]
    public void AnEntryPointingAtAnUndeclaredTracker_Rejects()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "custom", "tracker": "ghost", "date": "2026-08-11" }
            """)), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("declares no custom_trackers", plan.Errors[0].Message);
    }

    [Fact]
    public void AnUnknownIconOrColour_IsNormalized_NotRejected()
    {
        // Chrome, with documented fallbacks. Failing a whole file of medical history over
        // an emoji would be disproportionate.
        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "x", "name": "Limping", "shape": "tick", "icon": "🦄", "color": "chartreuse" } ],
              "entries": [] }
            """), WithPet());

        Assert.True(plan.IsValid);
        var definition = plan.Pets[0].Trackers[0].Definition!;
        Assert.Equal(CustomTrackerVisuals.DefaultIcon, definition.Icon);
        Assert.Equal(CustomTrackerVisuals.DefaultColorKey, definition.ColorKey);
        Assert.Equal(2, plan.AllNotices.Count(n => n.Kind == ImportNoticeKind.Normalized));
    }

    [Fact]
    public void ExceedingTheTrackerCap_Rejects()
    {
        var existing = Enumerable.Range(1, CustomTracker.MaxPerPet - 1)
            .Select(i => new ExistingTracker(i, 1, $"T{i}", CustomShape.Tick, IsArchived: false));

        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [
                { "ref": "a", "name": "New A", "shape": "tick" },
                { "ref": "b", "name": "New B", "shape": "tick" }
              ],
              "entries": [] }
            """), WithPet(trackers: existing));

        Assert.False(plan.IsValid);
        Assert.Contains("allows 10", plan.Errors[0].Message);
    }

    [Fact]
    public void ArchivedTrackersDoNotCountTowardTheCap()
    {
        var existing = Enumerable.Range(1, CustomTracker.MaxPerPet)
            .Select(i => new ExistingTracker(i, 1, $"T{i}", CustomShape.Tick, IsArchived: true));

        var plan = Validate(File("""
            { "match": "existing", "name": "Charly",
              "custom_trackers": [ { "ref": "a", "name": "New A", "shape": "tick" } ],
              "entries": [] }
            """), WithPet(trackers: existing));

        Assert.True(plan.IsValid);
    }

    // ── Values ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GlucoseWithoutFoodContext_Rejects()
    {
        // A glucose number without it is ambiguous to a vet, and guessing would be the
        // app inventing clinical context.
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "glucose", "date": "2026-08-11", "value": 8.2 }
            """)), WithPet());

        Assert.False(plan.IsValid);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public void ALevelOutsideOneToFive_Rejects(int level)
    {
        var plan = Validate(File(ExistingPetBlock(
            $$"""{ "type": "mood", "date": "2026-08-11", "level": {{level}} }""")), WithPet());

        Assert.False(plan.IsValid);
    }

    [Fact]
    public void AWeightWithARunawayDecimalPoint_Rejects()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "weight", "date": "2026-08-11", "value_kg": 1840 }
            """)), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("decimal point", plan.Errors[0].Message);
    }

    [Fact]
    public void ASeizureNeedsNoDurationOrType()
    {
        // "The owner didn't say" is the resting state and a normal answer, not a gap.
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "seizure", "date": "2026-08-10", "time": "20:00", "note": "Confused afterwards" }
            """)), WithPet());

        Assert.True(plan.IsValid);
        var seizure = Assert.Single(plan.Pets[0].Seizures);
        Assert.Null(seizure.DurationMinutes);
        Assert.Null(seizure.Type);
    }

    [Fact]
    public void ASubMinuteSeizureDuration_IsRejectedWithAdvice()
    {
        var plan = Validate(File(ExistingPetBlock("""
            { "type": "seizure", "date": "2026-08-10", "duration_minutes": 0 }
            """)), WithPet());

        Assert.False(plan.IsValid);
        Assert.Contains("Round a shorter one up to 1", plan.Errors[0].Message);
    }

    // ── The whole worked example from the brief ─────────────────────────────────

    [Fact]
    public void TheOwnersMessyWeek_ImportsAsExpected()
    {
        var plan = Validate("""
            {
              "felova_import_version": 1,
              "source_note": "Transcribed from owner's notes",
              "pets": [
                {
                  "match": "existing",
                  "name": "Charly",
                  "entries": [
                    { "type": "seizure", "date": "2026-08-10", "time": "20:00", "duration_minutes": 1,
                      "note": "Owner estimated about 45 seconds. Confused for around 10 minutes afterwards." },
                    { "type": "mood", "date": "2026-08-11", "level": 3, "note": "Seemed completely normal" },
                    { "type": "weight", "date": "2026-08-12", "value_kg": 18.4 },
                    { "type": "mood", "date": "2026-08-12", "level": 4 }
                  ]
                }
              ]
            }
            """, WithPet());

        Assert.True(plan.IsValid);
        Assert.Equal("Transcribed from owner's notes", plan.SourceNote);

        var pet = plan.Pets[0];
        Assert.Equal(1, pet.ExistingPetId);
        Assert.Equal(4, pet.EntryCount);
        Assert.Equal(0, pet.SkippedCount);

        // The 12th's mood and weight share one row; the 11th has its own.
        Assert.Equal(2, pet.Days.Count);
        Assert.Single(pet.Seizures);

        Assert.Equal((new DateTime(2026, 8, 10), new DateTime(2026, 8, 12)), pet.DateRange);
    }

    [Fact]
    public void ReimportingTheSameNotes_WritesNothingTheSecondTime()
    {
        // The property that matters most in practice: an AI asked the same question twice
        // never produces identical bytes, so the file hash cannot catch this — the
        // content rules have to.
        var json = """
            { "felova_import_version": 1, "pets": [
              { "match": "existing", "name": "Charly", "entries": [
                { "type": "mood", "date": "2026-08-11", "level": 4 },
                { "type": "seizure", "date": "2026-08-10", "time": "20:00", "duration_minutes": 1 }
              ] }
            ] }
            """;

        var snapshot = WithPet(
            days: new[] { new ExistingPetDay(1, new DateTime(2026, 8, 11), 77, HasMood: true, HasWeight: false, IsTombstone: false) },
            events: new[] { new ExistingEvent(1, ImportEntryType.Seizure, new DateTime(2026, 8, 10), new TimeSpan(20, 0, 0), 1m) });

        var plan = Validate(json, snapshot);

        Assert.True(plan.IsValid);
        Assert.Equal(0, plan.TotalEntryCount);
        Assert.Equal(2, plan.TotalSkippedCount);
        Assert.True(plan.IsEmpty);
    }
}

/// <summary>
/// The guide and the importer must not drift apart.
///
/// <para>AI/import-guide.md is the contract another AI generates against, so an example
/// in it that no longer validates is worse than no example: it teaches a file shape
/// Felova rejects, and nothing else in this repository would ever notice. This runs the
/// guide's own worked examples through the real validator.</para>
/// </summary>
public class ImportGuideTests
{
    private static readonly DateTime Today = new(2026, 8, 18);

    /// <summary>Walk up from the test binary to the repository root.</summary>
    private static string GuidePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "AI", "import-guide.md");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }

        throw new FileNotFoundException("AI/import-guide.md was not found above the test binary.");
    }

    /// <summary>Every fenced json block in the guide that is a COMPLETE file — the
    /// fragments that illustrate one section are deliberately skipped.</summary>
    public static IEnumerable<object[]> Examples()
    {
        var text = File.ReadAllText(GuidePath());
        var index = 0;

        foreach (var block in text.Split("```json").Skip(1))
        {
            var end = block.IndexOf("```", StringComparison.Ordinal);
            if (end < 0)
                continue;

            var json = block[..end].Trim();
            if (json.Contains("felova_import_version"))
                yield return new object[] { index++, json };
        }
    }

    [Theory]
    [MemberData(nameof(Examples))]
    public void EveryCompleteExampleInTheGuide_Validates(int index, string json)
    {
        Assert.True(ImportFileParser.TryParse(json, out var file, out var parseError),
            $"Guide example {index} did not parse: {parseError?.Message}");

        // The examples append to "Charly" and create "Mira"; both are covered by a device
        // that has Charly on it.
        var snapshot = new ImportSnapshot { Pets = new[] { new ExistingPet(1, "Charly", "Dog") } };
        var plan = ImportValidator.Validate(file, snapshot, Today);

        Assert.True(plan.IsValid,
            $"Guide example {index} is no longer valid: {string.Join("; ", plan.Errors.Select(e => e.ToString()))}");

        // An example that validates but writes nothing would be a broken teaching aid.
        Assert.True(plan.TotalEntryCount > 0, $"Guide example {index} imports no entries.");
    }

    [Fact]
    public void TheGuideIsFound_AndHasExamples()
    {
        // Guards the walk-up above: a guide that moved would otherwise make every test in
        // this class silently vacuous.
        Assert.NotEmpty(Examples());
    }
}

/// <summary>The parser's own tolerances — presentation forgiven, data never.</summary>
public class ImportFileParserTests
{
    [Fact]
    public void AMarkdownCodeFence_IsStripped()
    {
        // These files are copied out of a chat window, where JSON almost always arrives
        // fenced. Failing on it sends the owner back to fix a file that was correct.
        var text = "```json\n{ \"felova_import_version\": 1, \"pets\": [] }\n```";

        Assert.True(ImportFileParser.TryParse(text, out var file, out _));
        Assert.Equal(1, file.Version);
    }

    [Fact]
    public void TrailingCommasAndCommentsAreTolerated()
    {
        var text = """
            {
              // written by an assistant
              "felova_import_version": 1,
              "pets": [],
            }
            """;

        Assert.True(ImportFileParser.TryParse(text, out var file, out _));
        Assert.Equal(1, file.Version);
    }

    [Fact]
    public void BrokenJson_ReportsTheLine()
    {
        Assert.False(ImportFileParser.TryParse("{ \"felova_import_version\": }", out _, out var error));
        Assert.Contains("not valid JSON", error.Message);
    }

    [Fact]
    public void AnEmptyFile_IsNamedAsSuch()
    {
        Assert.False(ImportFileParser.TryParse("   ", out _, out var error));
        Assert.Contains("empty", error.Message);
    }
}
