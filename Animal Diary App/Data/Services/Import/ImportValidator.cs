namespace Animal_Diary_App.Data.Services.Import;

using System.Globalization;
using Animal_Diary_App.Data.Models;

// ─────────────────────────────────────────────────────────────────────────────
//  The whole file, judged before anything is written.
//
//  PURE by construction — no SQLite, no MAUI, no clock. Everything it needs about the
//  device arrives as an ImportSnapshot and "today" is a parameter, so the decisions
//  that matter (what gets written, what gets skipped, what rejects the file) are
//  testable without a database. That is the same reason PendingEngine and
//  ConstellationLayout are pure: these are the rules, and the rules are what break.
//
//  Two properties it must keep:
//
//   1. EVERY error is collected, not just the first. A file with forty bad dates should
//      say so once, not forty times across forty attempts.
//
//   2. A single error rejects the ENTIRE file. Validation still runs to completion (for
//      property 1), but nothing is committed. There is no "import the valid rows" path
//      — the rows came from one transcription pass, so a demonstrably wrong one is
//      evidence about the rest.
// ─────────────────────────────────────────────────────────────────────────────

public static class ImportValidator
{
    /// <summary>No pet diary predates this by any sane reading, so anything earlier is a
    /// mis-transcribed year rather than history. The upper bound
    /// (<see cref="ImportFormat.MaxFutureDays"/>) matters more, but a floor catches the
    /// other classic failure: a missing date deserializing to 0001-01-01.</summary>
    private static readonly DateTime EarliestDate = new(1990, 1, 1);

    /// <summary>Validate a parsed file against what the device already holds.</summary>
    /// <param name="today">The device's current date. A parameter rather than
    /// DateTime.Today so the future-date rule can be tested.</param>
    public static ImportPlan Validate(ImportFile file, ImportSnapshot snapshot, DateTime today)
    {
        var errors = new List<ImportError>();
        var fileNotices = new List<ImportNotice>();

        // ── Version first, and alone ────────────────────────────────────────────
        //
        // A version this build does not know makes every other check meaningless: the
        // fields may mean something else, and — worse — a field this build does not read
        // is indistinguishable from one the owner never recorded. So this returns rather
        // than accumulating, and it is the only check that does.
        if (file.Version is null)
        {
            return ImportPlan.Failed(new[]
            {
                new ImportError(ImportLocation.File,
                    $"Missing \"{ImportFormat.VersionKey}\". A Felova import file must declare its format version (currently {ImportFormat.Version})."),
            });
        }

        if (file.Version != ImportFormat.Version)
        {
            return ImportPlan.Failed(new[]
            {
                new ImportError(ImportLocation.File,
                    $"This file says {ImportFormat.VersionKey} = {file.Version}, but this build of Felova reads version {ImportFormat.Version}."),
            });
        }

        NoteUnknownFields(file.Unknown, ImportLocation.File, fileNotices);

        if (file.GeneratedAt is { Length: > 0 } generated &&
            !DateTimeOffset.TryParse(generated, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
        {
            fileNotices.Add(new ImportNotice(ImportNoticeKind.Ignored, ImportLocation.File,
                "generated_at is not a readable timestamp; it is shown for reference only and does not affect any entry."));
        }

        if (file.Pets is null || file.Pets.Count == 0)
        {
            errors.Add(new ImportError(ImportLocation.File, "The file contains no pets."));
            return ImportPlan.Failed(errors);
        }

        // ── Pet blocks ──────────────────────────────────────────────────────────

        var planned = new List<PlannedPet>();

        // Two blocks may not aim at the same animal. Beyond being an obvious mistake, it
        // would break the skip rules: each block resolves collisions against the snapshot
        // and neither can see rows the other is about to add, so a day claimed twice
        // would slip past both checks and produce exactly the duplicate this importer
        // exists to prevent.
        var claimedExistingIds = new Dictionary<int, int>();
        var claimedNewNames = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < file.Pets.Count; i++)
        {
            var block = file.Pets[i];
            var pet = ValidatePet(block, i, snapshot, today, errors, claimedExistingIds, claimedNewNames);
            if (pet is not null)
                planned.Add(pet);
        }

        return new ImportPlan
        {
            Errors = errors,
            Pets = errors.Count == 0 ? planned : Array.Empty<PlannedPet>(),
            FileNotices = fileNotices.ToList(),
            SourceNote = string.IsNullOrWhiteSpace(file.SourceNote) ? null : file.SourceNote!.Trim(),
        };
    }

    // ── One pet block ───────────────────────────────────────────────────────────

    private static PlannedPet? ValidatePet(
        ImportPet block,
        int index,
        ImportSnapshot snapshot,
        DateTime today,
        List<ImportError> errors,
        Dictionary<int, int> claimedExistingIds,
        Dictionary<string, int> claimedNewNames)
    {
        var notices = new List<ImportNotice>();
        NoteUnknownFields(block.Unknown, ImportLocation.Pet(index), notices);

        var name = (block.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            errors.Add(new ImportError(ImportLocation.PetField(index, "name"), "A pet needs a name."));

        var matchParsed = ImportFormat.TryParsePetMatch(block.Match, out var match);
        if (!matchParsed)
        {
            errors.Add(new ImportError(ImportLocation.PetField(index, "match"),
                block.Match is null
                    ? "Missing \"match\". Say \"existing\" to add to a pet already in Felova, or \"new\" to create one."
                    : $"Unknown match \"{block.Match}\". Use \"existing\" or \"new\"."));
        }

        // Resolve the target before anything else needs it — the entry rules below key
        // their collision checks off the pet id. Skipped when the block has not said
        // enough to have a target at all; the entry pass still runs, so a file with a
        // missing name still reports everything else wrong with it in one go.
        var existingPetId = 0;
        PlannedNewPet? newPet = null;

        if (matchParsed && name.Length > 0)
        {
            switch (match)
            {
                case ImportPetMatch.Existing:
                    existingPetId = ResolveExisting(block, index, name, snapshot, errors);
                    break;

                case ImportPetMatch.New:
                    newPet = BuildNewPet(block, index, name, today, snapshot, errors, notices);
                    break;
            }
        }

        // Claim the target, so a second block cannot aim at it.
        if (existingPetId != 0)
        {
            if (claimedExistingIds.TryGetValue(existingPetId, out var firstBlock))
            {
                errors.Add(new ImportError(ImportLocation.Pet(index),
                    $"This pet is already the target of pets[{firstBlock}]. Put all of one animal's entries in a single pet block."));
            }
            else
            {
                claimedExistingIds[existingPetId] = index;
            }
        }
        else if (newPet is not null)
        {
            if (claimedNewNames.TryGetValue(newPet.Name, out var firstBlock))
            {
                errors.Add(new ImportError(ImportLocation.Pet(index),
                    $"A second new pet named \"{newPet.Name}\" — pets[{firstBlock}] already creates one. Put all of one animal's entries in a single pet block."));
            }
            else
            {
                claimedNewNames[newPet.Name] = index;
            }
        }

        var plan = new PlannedPet
        {
            Match = match,
            Name = name.Length > 0 ? name : "(unnamed)",
            ExistingPetId = existingPetId,
            NewPet = newPet,
        };
        plan.Notices.AddRange(notices);

        var trackers = ValidateTrackers(block, index, existingPetId, snapshot, errors, plan);
        ValidateEntries(block, index, existingPetId, snapshot, today, errors, plan, trackers);

        return plan;
    }

    /// <summary>Find the pet a block claims to append to. Ambiguity is an error rather
    /// than a guess: two animals with the same name is the exact case where writing a
    /// seizure onto the wrong one is both easy and invisible.</summary>
    private static int ResolveExisting(
        ImportPet block,
        int index,
        string name,
        ImportSnapshot snapshot,
        List<ImportError> errors)
    {
        if (name.Length == 0)
            return 0;

        var matches = snapshot.Pets
            .Where(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        // A species, when the file supplies one, is only ever a tie-breaker — never a
        // filter that could reduce a correct single match to none.
        var species = ImportFormat.NormalizeSpecies(block.Species);
        if (matches.Count > 1 && species.Length > 0)
        {
            var narrowed = matches
                .Where(p => string.Equals(ImportFormat.NormalizeSpecies(p.Species), species, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (narrowed.Count == 1)
                matches = narrowed;
        }

        if (matches.Count == 1)
            return matches[0].Id;

        if (matches.Count == 0)
        {
            errors.Add(new ImportError(ImportLocation.PetField(index, "name"),
                snapshot.Pets.Count == 0
                    ? $"No pet named \"{name}\" — this device has no pets yet. Use \"match\": \"new\" to create one."
                    : $"No pet named \"{name}\". Pets on this device: {string.Join(", ", snapshot.Pets.Select(p => p.Name))}. Use \"match\": \"new\" to create one."));
            return 0;
        }

        errors.Add(new ImportError(ImportLocation.PetField(index, "name"),
            $"\"{name}\" matches {matches.Count} pets on this device, so Felova cannot tell which one these entries belong to. Add a \"species\" to tell them apart, or rename one of the pets."));
        return 0;
    }

    private static PlannedNewPet? BuildNewPet(
        ImportPet block,
        int index,
        string name,
        DateTime today,
        ImportSnapshot snapshot,
        List<ImportError> errors,
        List<ImportNotice> notices)
    {
        var ok = name.Length > 0;

        var species = ImportFormat.NormalizeSpecies(block.Species);
        if (species.Length == 0)
        {
            errors.Add(new ImportError(ImportLocation.PetField(index, "species"),
                $"A new pet needs a species. Use one of: {string.Join(", ", ImportFormat.CanonicalSpecies)} — or any word if none fit."));
            ok = false;
        }

        // The birth year is the one part of a birthday this app always requires; month
        // and day stay null when unknown and are never fabricated (AI/domain.md).
        if (block.BirthYear is not int year)
        {
            errors.Add(new ImportError(ImportLocation.PetField(index, "birth_year"),
                "A new pet needs a birth_year. If the owner only knows roughly, use their best year and leave birth_month and birth_day out — Felova never invents them."));
            ok = false;
        }
        else if (year < EarliestDate.Year || year > today.Year)
        {
            errors.Add(new ImportError(ImportLocation.PetField(index, "birth_year"),
                $"birth_year {year} is not a year this pet could have been born in (expected {EarliestDate.Year}–{today.Year})."));
            ok = false;
        }

        int? month = null;
        if (block.BirthMonth is int m)
        {
            if (m is < 1 or > 12)
            {
                errors.Add(new ImportError(ImportLocation.PetField(index, "birth_month"),
                    $"birth_month {m} is not a month. Leave it out when the owner does not know."));
                ok = false;
            }
            else
            {
                month = m;
            }
        }

        int? day = null;
        if (block.BirthDay is int d)
        {
            if (month is null)
            {
                // A day without a month says nothing, and storing it would make the age
                // calculation read a value it can never use.
                notices.Add(new ImportNotice(ImportNoticeKind.Ignored, ImportLocation.PetField(index, "birth_day"),
                    "birth_day was given without a birth_month, so it cannot be used and will not be stored."));
            }
            else if (d < 1 || d > DateTime.DaysInMonth(block.BirthYear ?? today.Year, month.Value))
            {
                errors.Add(new ImportError(ImportLocation.PetField(index, "birth_day"),
                    $"birth_day {d} is not a day in month {month.Value}."));
                ok = false;
            }
            else
            {
                day = d;
            }
        }

        var conditions = new List<string>();
        foreach (var raw in block.Conditions ?? new List<string>())
        {
            if (!ImportFormat.IsKnownCondition(raw))
            {
                errors.Add(new ImportError(ImportLocation.PetField(index, "conditions"),
                    $"Unknown condition \"{raw}\". Felova knows: {string.Join(", ", ImportFormat.ConditionIds)}. Leave the array out if none apply."));
                ok = false;
                continue;
            }

            var normalized = ImportFormat.NormalizeCondition(raw);
            if (!conditions.Contains(normalized))
                conditions.Add(normalized);
        }

        // Creating a second animal with a name the device already uses is legal — people
        // do re-use names — but it is also exactly what a wrong "new" looks like, so the
        // owner is told before they confirm rather than after.
        if (name.Length > 0 && snapshot.Pets.Any(p => string.Equals(p.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
        {
            notices.Add(new ImportNotice(ImportNoticeKind.Normalized, ImportLocation.PetField(index, "name"),
                $"This file creates a NEW pet named \"{name}\", and a pet with that name already exists. Cancel if these entries belong to the existing one."));
        }

        return ok
            ? new PlannedNewPet(name, species, block.BirthYear!.Value, month, day, conditions)
            : null;
    }

    // ── Custom trackers ─────────────────────────────────────────────────────────

    /// <summary>Resolve the block's tracker definitions, returning them by ref so the
    /// entry pass can look up a shape without re-searching.</summary>
    private static Dictionary<string, PlannedTracker> ValidateTrackers(
        ImportPet block,
        int index,
        int existingPetId,
        ImportSnapshot snapshot,
        List<ImportError> errors,
        PlannedPet plan)
    {
        var byRef = new Dictionary<string, PlannedTracker>(StringComparer.OrdinalIgnoreCase);
        var declared = block.CustomTrackers ?? new List<ImportCustomTracker>();
        if (declared.Count == 0)
            return byRef;

        var existing = snapshot.Trackers.Where(t => t.PetId == existingPetId).ToList();

        for (var t = 0; t < declared.Count; t++)
        {
            var def = declared[t];
            var where = ImportLocation.PetField(index, $"custom_trackers[{t}]");
            NoteUnknownFields(def.Unknown, where, plan.Notices);

            var handle = (def.Ref ?? string.Empty).Trim();
            var name = (def.Name ?? string.Empty).Trim();

            if (handle.Length == 0)
            {
                errors.Add(new ImportError(where, "A custom tracker needs a \"ref\" — a short handle its entries point at."));
                continue;
            }

            if (byRef.ContainsKey(handle))
            {
                errors.Add(new ImportError(where, $"Duplicate tracker ref \"{handle}\" — each ref must be unique within a pet."));
                continue;
            }

            if (name.Length == 0)
            {
                errors.Add(new ImportError(where, "A custom tracker needs a \"name\" — what the owner calls it."));
                continue;
            }

            if (!ImportFormat.TryParseShape(def.Shape, out var shape))
            {
                errors.Add(new ImportError(where,
                    def.Shape is null
                        ? "A custom tracker needs a \"shape\": \"tick\" (it happened) or \"amount\" (a number)."
                        : $"Unknown shape \"{def.Shape}\". Use \"tick\" or \"amount\"."));
                continue;
            }

            var unit = (def.Unit ?? string.Empty).Trim();
            if (shape == CustomShape.Amount && unit.Length == 0)
            {
                errors.Add(new ImportError(where,
                    "An \"amount\" tracker needs a \"unit\" — the owner's own word for what the number counts (\"min\", \"km\", \"bowls\")."));
                continue;
            }

            if (shape == CustomShape.Tick && unit.Length > 0)
            {
                plan.Notices.Add(new ImportNotice(ImportNoticeKind.Ignored, where,
                    $"\"{name}\" is a tick tracker, so its unit \"{unit}\" is not stored."));
                unit = string.Empty;
            }

            // Reuse a tracker the pet already has rather than creating a second one with
            // the same name. Archived ones count: matching them is what stops an import
            // resurrecting a duplicate of something the owner deliberately retired.
            var match = existing.FirstOrDefault(e =>
                string.Equals(e.Name.Trim(), name, StringComparison.OrdinalIgnoreCase));

            if (match is not null)
            {
                if (match.Shape != shape)
                {
                    errors.Add(new ImportError(where,
                        $"\"{name}\" already exists on this pet as a \"{Wire(match.Shape)}\" tracker, but this file describes it as \"{Wire(shape)}\". Rename one of them, or match the existing shape."));
                    continue;
                }

                var resolved = new PlannedTracker(handle, match.Id, null, match.IsArchived, shape, t);
                byRef[handle] = resolved;
                plan.Trackers.Add(resolved);

                if (match.IsArchived)
                {
                    plan.Notices.Add(new ImportNotice(ImportNoticeKind.Normalized, where,
                        $"\"{name}\" is retired on this pet. Its imported entries are stored and appear in the timeline and vet report, but it stays retired and off the Journal's chip row."));
                }

                continue;
            }

            // ── A new definition ────────────────────────────────────────────────

            var kind = ImportFormat.DefaultCustomCadence;
            if (def.Cadence is { Length: > 0 })
            {
                if (!ImportFormat.TryParseCadence(def.Cadence, out kind))
                {
                    errors.Add(new ImportError(where,
                        $"Unknown cadence \"{def.Cadence}\". Use per_day, daily, weekly, twice_weekly, as_needed or event — or leave it out to record without ever being asked for it."));
                    continue;
                }
            }

            var perDay = 0;
            if (kind == TrackerKind.PerDay)
            {
                if (def.PerDayCount is not int count || count < 1)
                {
                    errors.Add(new ImportError(where,
                        "A \"per_day\" tracker needs a per_day_count of at least 1."));
                    continue;
                }
                perDay = count;
            }
            else if (def.PerDayCount is int stray && stray != 0)
            {
                plan.Notices.Add(new ImportNotice(ImportNoticeKind.Ignored, where,
                    $"per_day_count only applies to a \"per_day\" cadence, so it is not stored for \"{name}\"."));
            }

            // Icon and colour are chrome, and both have documented fallbacks in
            // CustomTrackerVisuals. Normalizing beats rejecting: failing a whole file of
            // medical history over an emoji outside the picker's set would be
            // disproportionate, and the guide lists the supported ones so a compliant
            // file never lands here.
            var icon = (def.Icon ?? string.Empty).Trim();
            if (icon.Length > 0 && !CustomTrackerVisuals.Icons.Contains(icon))
            {
                plan.Notices.Add(new ImportNotice(ImportNoticeKind.Normalized, where,
                    $"\"{icon}\" is not one of Felova's tracker icons, so \"{name}\" uses {CustomTrackerVisuals.DefaultIcon} instead."));
                icon = string.Empty;
            }

            var color = (def.Color ?? string.Empty).Trim().ToLowerInvariant();
            if (color.Length > 0 && !CustomTrackerVisuals.Palette.Any(s => s.Key == color))
            {
                plan.Notices.Add(new ImportNotice(ImportNoticeKind.Normalized, where,
                    $"\"{color}\" is not one of Felova's tracker colours, so \"{name}\" uses {CustomTrackerVisuals.DefaultColorKey}."));
                color = string.Empty;
            }

            var definition = new CustomTracker
            {
                Name = name,
                Icon = icon.Length > 0 ? icon : CustomTrackerVisuals.DefaultIcon,
                ColorKey = color.Length > 0 ? color : CustomTrackerVisuals.DefaultColorKey,
                Shape = shape,
                Unit = unit,
                Kind = kind,
                PerDayCount = perDay,
                IncludeInReport = def.IncludeInReport ?? true,
            };

            var plannedTracker = new PlannedTracker(handle, 0, definition, false, shape, t);
            byRef[handle] = plannedTracker;
            plan.Trackers.Add(plannedTracker);
        }

        // The in-app cap, enforced here too. Without it an import would walk straight
        // past a limit the create sheet enforces, and the owner would end up with a chip
        // row they cannot get back under the cap without archiving.
        var liveExisting = existing.Count(e => !e.IsArchived);
        var creating = plan.Trackers.Count(x => x.IsNew);
        if (liveExisting + creating > CustomTracker.MaxPerPet)
        {
            errors.Add(new ImportError(ImportLocation.PetField(index, "custom_trackers"),
                $"This would leave {plan.Name} with {liveExisting + creating} active custom trackers; Felova allows {CustomTracker.MaxPerPet}. Remove some from the file, or archive some in the app first."));
        }

        return byRef;
    }

    private static string Wire(CustomShape shape) => shape == CustomShape.Tick ? "tick" : "amount";

    // ── Entries ─────────────────────────────────────────────────────────────────

    private static void ValidateEntries(
        ImportPet block,
        int index,
        int existingPetId,
        ImportSnapshot snapshot,
        DateTime today,
        List<ImportError> errors,
        PlannedPet plan,
        Dictionary<string, PlannedTracker> trackers)
    {
        var entries = block.Entries ?? new List<ImportEntry>();
        if (entries.Count == 0)
        {
            // Not an error on its own: a block may exist purely to create a pet, or to
            // add a tracker. A file where NOTHING happens is caught by the preview.
            return;
        }

        // Existing state, indexed once.
        var petDays = snapshot.PetDays
            .Where(d => d.PetId == existingPetId)
            .ToDictionary(d => d.Date.Date);

        var levelDays = snapshot.LevelDays
            .Where(d => d.PetId == existingPetId)
            .ToDictionary(d => (d.Type, d.Date.Date));

        var storedEvents = new HashSet<(ImportEntryType, DateTime, TimeSpan, decimal, int)>(
            snapshot.Events
                .Where(e => e.PetId == existingPetId)
                .Select(e => (e.Type, e.Date.Date, e.Time, e.Value, e.TrackerId)));

        // Days this file has already claimed, so two moods for one date contradict each
        // other loudly instead of one silently winning.
        var daysInFile = new Dictionary<DateTime, PlannedPetDay>();
        var moodClaimed = new HashSet<DateTime>();
        var weightClaimed = new HashSet<DateTime>();
        var levelClaimed = new HashSet<(ImportEntryType, DateTime)>();

        for (var e = 0; e < entries.Count; e++)
        {
            var entry = entries[e];
            var where = ImportLocation.Entry(index, e);
            NoteUnknownFields(entry.Unknown, where, plan.Notices);

            if (!ImportFormat.TryParseEntryType(entry.Type, out var type))
            {
                errors.Add(new ImportError(ImportLocation.EntryField(index, e, "type"),
                    entry.Type is null
                        ? $"An entry needs a \"type\". Felova reads: {string.Join(", ", ImportFormat.EntryTypeNames)}."
                        : $"Unknown entry type \"{entry.Type}\". Felova reads: {string.Join(", ", ImportFormat.EntryTypeNames)}. Anything that does not fit one of these belongs in a custom tracker, or in an entry's note."));
                continue;
            }

            if (!TryReadDate(entry.Date, index, e, today, errors, out var date))
                continue;

            if (!TryReadTime(entry.Time, index, e, errors, out var time))
                continue;

            switch (type)
            {
                case ImportEntryType.Weight:
                    ReadWeight(entry, index, e, date, time, errors, plan, petDays, daysInFile, weightClaimed);
                    break;

                case ImportEntryType.Mood:
                    ReadMood(entry, index, e, date, time, errors, plan, petDays, daysInFile, moodClaimed);
                    break;

                case ImportEntryType.Glucose:
                    ReadGlucose(entry, index, e, date, time, errors, plan, storedEvents);
                    break;

                case ImportEntryType.AppetiteLevel:
                    ReadAppetiteLevel(entry, index, e, date, time, errors, plan, levelDays, levelClaimed);
                    break;

                case ImportEntryType.AppetiteAmount:
                    ReadAppetiteAmount(entry, index, e, date, time, errors, plan, storedEvents);
                    break;

                case ImportEntryType.WaterLevel:
                    ReadWaterLevel(entry, index, e, date, time, errors, plan, levelDays, levelClaimed);
                    break;

                case ImportEntryType.WaterAmount:
                    ReadWaterAmount(entry, index, e, date, time, errors, plan, storedEvents);
                    break;

                case ImportEntryType.Seizure:
                    ReadSeizure(entry, index, e, date, time, errors, plan, storedEvents);
                    break;

                case ImportEntryType.Custom:
                    ReadCustom(entry, index, e, date, time, errors, plan, trackers, storedEvents);
                    break;
            }
        }
    }

    // ── Shared field readers ────────────────────────────────────────────────────

    private static bool TryReadDate(
        string? raw, int petIndex, int entryIndex, DateTime today, List<ImportError> errors, out DateTime date)
    {
        date = default;
        var where = ImportLocation.EntryField(petIndex, entryIndex, "date");

        if (string.IsNullOrWhiteSpace(raw))
        {
            errors.Add(new ImportError(where,
                $"An entry needs a \"date\" in {ImportFormat.DateFormat} form. Never guess one — leave the entry out instead."));
            return false;
        }

        if (!DateTime.TryParseExact(raw.Trim(), ImportFormat.DateFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            errors.Add(new ImportError(where,
                $"\"{raw}\" is not a date Felova can read. Use {ImportFormat.DateFormat} (for example 2026-08-10)."));
            return false;
        }

        var latest = today.Date.AddDays(ImportFormat.MaxFutureDays);
        if (parsed.Date > latest)
        {
            errors.Add(new ImportError(where,
                $"{raw} is in the future. Entries record what already happened."));
            return false;
        }

        if (parsed.Date < EarliestDate)
        {
            errors.Add(new ImportError(where,
                $"{raw} is implausibly early — check the year was transcribed correctly."));
            return false;
        }

        date = parsed.Date;
        return true;
    }

    /// <summary>An absent time is legitimate and common: notes rarely say when. It comes
    /// back as null, and each store decides how to hold that — see the callers.</summary>
    private static bool TryReadTime(
        string? raw, int petIndex, int entryIndex, List<ImportError> errors, out TimeSpan? time)
    {
        time = null;
        if (string.IsNullOrWhiteSpace(raw))
            return true;

        if (!DateTime.TryParseExact(raw.Trim(), ImportFormat.TimeFormat,
                CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            errors.Add(new ImportError(ImportLocation.EntryField(petIndex, entryIndex, "time"),
                $"\"{raw}\" is not a time Felova can read. Use 24-hour {ImportFormat.TimeFormat} (for example 20:00), or leave it out when the notes do not say."));
            return false;
        }

        time = parsed.TimeOfDay;
        return true;
    }

    private static bool TryReadLevel(
        int? raw, int petIndex, int entryIndex, string field, List<ImportError> errors, out int level)
    {
        level = 0;
        if (raw is not int value)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(petIndex, entryIndex, field),
                $"This entry needs a \"{field}\" from {ImportFormat.MinLevel} to {ImportFormat.MaxLevel}."));
            return false;
        }

        if (value < ImportFormat.MinLevel || value > ImportFormat.MaxLevel)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(petIndex, entryIndex, field),
                $"\"{field}\" is {value}; Felova's scale runs {ImportFormat.MinLevel}–{ImportFormat.MaxLevel}."));
            return false;
        }

        level = value;
        return true;
    }

    private static bool TryReadPositive(
        decimal? raw, decimal max, int petIndex, int entryIndex, string field, List<ImportError> errors, out decimal value)
    {
        value = 0m;
        if (raw is not decimal number)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(petIndex, entryIndex, field),
                $"This entry needs a \"{field}\"."));
            return false;
        }

        if (number <= 0m)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(petIndex, entryIndex, field),
                $"\"{field}\" is {number}; a recorded measurement is greater than zero."));
            return false;
        }

        if (number > max)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(petIndex, entryIndex, field),
                $"\"{field}\" is {number}, which is beyond anything Felova expects (max {max}). Check the units and the decimal point."));
            return false;
        }

        value = number;
        return true;
    }

    // ── Per-type readers ────────────────────────────────────────────────────────

    /// <summary>The day's PetEntry write, created or reused. Mood and weight share this
    /// row, so both readers funnel through here rather than each inventing a row.</summary>
    private static PlannedPetDay DayFor(
        DateTime date,
        PlannedPet plan,
        Dictionary<DateTime, ExistingPetDay> petDays,
        Dictionary<DateTime, PlannedPetDay> daysInFile)
    {
        if (daysInFile.TryGetValue(date, out var already))
            return already;

        petDays.TryGetValue(date, out var existing);
        var day = new PlannedPetDay
        {
            Date = date,
            RowId = existing?.RowId ?? 0,
            ReviveTombstone = existing?.IsTombstone ?? false,
        };

        daysInFile[date] = day;
        plan.Days.Add(day);
        return day;
    }

    private static void ReadWeight(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        Dictionary<DateTime, ExistingPetDay> petDays,
        Dictionary<DateTime, PlannedPetDay> daysInFile,
        HashSet<DateTime> claimed)
    {
        if (!TryReadPositive(entry.ValueKg, ImportFormat.MaxWeightKg, i, e, "value_kg", errors, out var kg))
            return;

        if (!claimed.Add(date))
        {
            errors.Add(new ImportError(ImportLocation.Entry(i, e),
                $"A second weight for {date:yyyy-MM-dd}. Felova keeps one weight per day — decide which reading is right."));
            return;
        }

        // Never overwrite what the owner already recorded. The day's other half (mood) is
        // untouched either way.
        if (petDays.TryGetValue(date, out var existing) && existing.HasWeight && !existing.IsTombstone)
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.SlotOccupied, ImportLocation.Entry(i, e),
                $"{date:yyyy-MM-dd} already has a weight recorded, so this one is not imported."));
            return;
        }

        var day = DayFor(date, plan, petDays, daysInFile);
        day.Weight = kg;
        day.WeightTimeTicks = time?.Ticks;
    }

    private static void ReadMood(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        Dictionary<DateTime, ExistingPetDay> petDays,
        Dictionary<DateTime, PlannedPetDay> daysInFile,
        HashSet<DateTime> claimed)
    {
        if (!TryReadLevel(entry.Level, i, e, "level", errors, out var level))
            return;

        if (!claimed.Add(date))
        {
            errors.Add(new ImportError(ImportLocation.Entry(i, e),
                $"A second mood for {date:yyyy-MM-dd}. Felova keeps one mood per day — decide which reading is right."));
            return;
        }

        if (petDays.TryGetValue(date, out var existing) && existing.HasMood && !existing.IsTombstone)
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.SlotOccupied, ImportLocation.Entry(i, e),
                $"{date:yyyy-MM-dd} already has a mood recorded, so this one is not imported."));
            return;
        }

        var day = DayFor(date, plan, petDays, daysInFile);
        day.MoodLevel = level;
        day.MoodNote = (entry.Note ?? string.Empty).Trim();
        day.IncludeNoteInVetReport = entry.IncludeNoteInVetReport ?? false;
        day.MoodTimeTicks = time?.Ticks;
    }

    private static void ReadGlucose(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        HashSet<(ImportEntryType, DateTime, TimeSpan, decimal, int)> stored)
    {
        if (!TryReadPositive(entry.Value, ImportFormat.MaxGlucose, i, e, "value", errors, out var value))
            return;

        // The one bit of context that decides how a reading is read. Required, because a
        // glucose number without it is genuinely ambiguous to a vet — and guessing would
        // be the app inventing clinical context.
        if (!ImportFormat.TryParseFoodContext(entry.Context, out var context))
        {
            errors.Add(new ImportError(ImportLocation.EntryField(i, e, "context"),
                entry.Context is null
                    ? "A glucose reading needs a \"context\": \"before_food\" or \"after_food\". If the notes do not say, leave the reading out rather than guessing."
                    : $"Unknown context \"{entry.Context}\". Use \"before_food\" or \"after_food\"."));
            return;
        }

        var at = time ?? TimeSpan.Zero;
        if (!stored.Add((ImportEntryType.Glucose, date, at, value, 0)))
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.AlreadyPresent, ImportLocation.Entry(i, e),
                $"A glucose reading of {value} at {date:yyyy-MM-dd} {at:hh\\:mm} is already recorded, so it is not imported again."));
            return;
        }

        plan.Glucose.Add(new GlucoseEntry
        {
            Date = date,
            Time = at,
            Value = value,
            Context = context,
        });
    }

    private static void ReadAppetiteLevel(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        Dictionary<(ImportEntryType, DateTime), ExistingLevelDay> levelDays,
        HashSet<(ImportEntryType, DateTime)> claimed)
    {
        if (!TryReadLevel(entry.Level, i, e, "level", errors, out var level))
            return;

        if (!claimed.Add((ImportEntryType.AppetiteLevel, date)))
        {
            errors.Add(new ImportError(ImportLocation.Entry(i, e),
                $"A second appetite reading for {date:yyyy-MM-dd}. Felova keeps one per day — use appetite_amount for individual meals."));
            return;
        }

        var revive = 0;
        if (levelDays.TryGetValue((ImportEntryType.AppetiteLevel, date), out var existing))
        {
            if (!existing.IsTombstone)
            {
                plan.Notices.Add(new ImportNotice(ImportNoticeKind.SlotOccupied, ImportLocation.Entry(i, e),
                    $"{date:yyyy-MM-dd} already has an appetite reading, so this one is not imported."));
                return;
            }

            revive = existing.RowId;
        }

        plan.AppetiteLevels.Add(new PlannedLevelRow<AppetiteEntry>(
            new AppetiteEntry
            {
                Date = date,
                Time = time ?? TimeSpan.Zero,
                Level = level,
                Food = (entry.Food ?? string.Empty).Trim(),
            },
            revive));
    }

    private static void ReadAppetiteAmount(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        HashSet<(ImportEntryType, DateTime, TimeSpan, decimal, int)> stored)
    {
        if (!TryReadPositive(entry.Grams, ImportFormat.MaxGrams, i, e, "grams", errors, out var grams))
            return;

        var at = time ?? TimeSpan.Zero;
        if (!stored.Add((ImportEntryType.AppetiteAmount, date, at, grams, 0)))
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.AlreadyPresent, ImportLocation.Entry(i, e),
                $"{grams} g at {date:yyyy-MM-dd} {at:hh\\:mm} is already recorded, so it is not imported again."));
            return;
        }

        plan.AppetiteAmounts.Add(new AppetiteAmountEntry
        {
            Date = date,
            Time = at,
            Grams = grams,
            Food = (entry.Food ?? string.Empty).Trim(),
        });
    }

    private static void ReadWaterLevel(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        Dictionary<(ImportEntryType, DateTime), ExistingLevelDay> levelDays,
        HashSet<(ImportEntryType, DateTime)> claimed)
    {
        if (!TryReadLevel(entry.Level, i, e, "level", errors, out var level))
            return;

        if (!claimed.Add((ImportEntryType.WaterLevel, date)))
        {
            errors.Add(new ImportError(ImportLocation.Entry(i, e),
                $"A second water reading for {date:yyyy-MM-dd}. Felova keeps one per day — use water_amount for individual drinks."));
            return;
        }

        var revive = 0;
        if (levelDays.TryGetValue((ImportEntryType.WaterLevel, date), out var existing))
        {
            if (!existing.IsTombstone)
            {
                plan.Notices.Add(new ImportNotice(ImportNoticeKind.SlotOccupied, ImportLocation.Entry(i, e),
                    $"{date:yyyy-MM-dd} already has a water reading, so this one is not imported."));
                return;
            }

            revive = existing.RowId;
        }

        plan.WaterLevels.Add(new PlannedLevelRow<WaterLevelEntry>(
            new WaterLevelEntry
            {
                Date = date,
                Time = time ?? TimeSpan.Zero,
                Level = level,
            },
            revive));
    }

    private static void ReadWaterAmount(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        HashSet<(ImportEntryType, DateTime, TimeSpan, decimal, int)> stored)
    {
        if (!TryReadPositive(entry.Ml, ImportFormat.MaxMilliliters, i, e, "ml", errors, out var ml))
            return;

        var at = time ?? TimeSpan.Zero;
        if (!stored.Add((ImportEntryType.WaterAmount, date, at, ml, 0)))
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.AlreadyPresent, ImportLocation.Entry(i, e),
                $"{ml} ml at {date:yyyy-MM-dd} {at:hh\\:mm} is already recorded, so it is not imported again."));
            return;
        }

        plan.WaterAmounts.Add(new WaterAmountEntry
        {
            Date = date,
            Time = at,
            AmountMl = ml,
        });
    }

    private static void ReadSeizure(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        HashSet<(ImportEntryType, DateTime, TimeSpan, decimal, int)> stored)
    {
        int? duration = null;
        if (entry.DurationMinutes is int minutes)
        {
            if (minutes < ImportFormat.MinSeizureMinutes || minutes > ImportFormat.MaxSeizureMinutes)
            {
                errors.Add(new ImportError(ImportLocation.EntryField(i, e, "duration_minutes"),
                    $"duration_minutes is {minutes}; Felova stores whole minutes from {ImportFormat.MinSeizureMinutes} to {ImportFormat.MaxSeizureMinutes}. Round a shorter one up to 1 and put the owner's exact wording in the note, or leave it out when they did not time it."));
                return;
            }
            duration = minutes;
        }

        SeizureType? seizureType = null;
        if (entry.SeizureType is { Length: > 0 })
        {
            if (!ImportFormat.TryParseSeizureType(entry.SeizureType, out var parsed))
            {
                errors.Add(new ImportError(ImportLocation.EntryField(i, e, "seizure_type"),
                    $"Unknown seizure_type \"{entry.SeizureType}\". Use generalized, focal or focal_to_generalized — or leave it out, which is a normal answer and not a missing field."));
                return;
            }
            seizureType = parsed;
        }

        var at = time ?? TimeSpan.Zero;
        if (!stored.Add((ImportEntryType.Seizure, date, at, duration ?? 0, 0)))
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.AlreadyPresent, ImportLocation.Entry(i, e),
                $"A seizure at {date:yyyy-MM-dd} {at:hh\\:mm} is already recorded, so it is not imported again."));
            return;
        }

        plan.Seizures.Add(new SeizureEntry
        {
            Date = date,
            Time = at,
            DurationMinutes = duration,
            Type = seizureType,
            Note = (entry.Note ?? string.Empty).Trim(),
        });
    }

    private static void ReadCustom(
        ImportEntry entry, int i, int e, DateTime date, TimeSpan? time,
        List<ImportError> errors, PlannedPet plan,
        Dictionary<string, PlannedTracker> trackers,
        HashSet<(ImportEntryType, DateTime, TimeSpan, decimal, int)> stored)
    {
        var handle = (entry.Tracker ?? string.Empty).Trim();
        if (handle.Length == 0)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(i, e, "tracker"),
                "A custom entry needs a \"tracker\" naming one of this pet's custom_trackers by its ref."));
            return;
        }

        if (!trackers.TryGetValue(handle, out var tracker))
        {
            errors.Add(new ImportError(ImportLocation.EntryField(i, e, "tracker"),
                trackers.Count == 0
                    ? $"This entry points at tracker \"{handle}\", but the pet declares no custom_trackers."
                    : $"No custom tracker with ref \"{handle}\". This pet declares: {string.Join(", ", trackers.Keys)}."));
            return;
        }

        decimal? amount = null;
        if (tracker.Shape == CustomShape.Amount)
        {
            if (!TryReadPositive(entry.Amount, ImportFormat.MaxCustomAmount, i, e, "amount", errors, out var value))
                return;
            amount = value;
        }
        else if (entry.Amount is not null)
        {
            errors.Add(new ImportError(ImportLocation.EntryField(i, e, "amount"),
                $"\"{handle}\" is a tick tracker — it records that something happened, not how much. Put the detail in the note, or declare the tracker as \"amount\" with a unit."));
            return;
        }

        var at = time ?? TimeSpan.Zero;

        // Custom events fingerprint on the tracker too: two different trackers ticked at
        // the same minute are two different facts. An existing tracker uses its real row
        // id; one being created cannot collide with anything stored, so its negated
        // ordinal stands in — stable within the run, and never equal to a real id.
        var trackerKey = tracker.ExistingId != 0 ? tracker.ExistingId : -(tracker.Ordinal + 1);
        if (!stored.Add((ImportEntryType.Custom, date, at, amount ?? 0m, trackerKey)))
        {
            plan.Notices.Add(new ImportNotice(ImportNoticeKind.AlreadyPresent, ImportLocation.Entry(i, e),
                $"An entry for \"{handle}\" at {date:yyyy-MM-dd} {at:hh\\:mm} is already recorded, so it is not imported again."));
            return;
        }

        plan.CustomEntries.Add(new PlannedCustomEntry(handle, new CustomEntry
        {
            Date = date,
            Time = at,
            Amount = amount,
            Note = (entry.Note ?? string.Empty).Trim(),
        }));
    }

    // ── Unknown fields ──────────────────────────────────────────────────────────

    /// <summary>Report, never reject. A field this build does not know is most likely a
    /// newer format's addition, and dropping it silently would let a file claim to have
    /// imported something it did not. The owner sees the name and decides.</summary>
    private static void NoteUnknownFields(
        Dictionary<string, System.Text.Json.JsonElement>? unknown,
        ImportLocation where,
        List<ImportNotice> notices)
    {
        if (unknown is null || unknown.Count == 0)
            return;

        notices.Add(new ImportNotice(ImportNoticeKind.UnknownField, where,
            $"Felova does not read {string.Join(", ", unknown.Keys.Select(k => $"\"{k}\""))} here, so it is ignored."));
    }
}
