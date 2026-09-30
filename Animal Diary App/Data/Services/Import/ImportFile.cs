namespace Animal_Diary_App.Data.Services.Import;

using System.Text.Json;
using System.Text.Json.Serialization;

// ─────────────────────────────────────────────────────────────────────────────
//  The wire shape of an import file: deserialization targets and nothing else.
//
//  Every field is NULLABLE, including the ones the format requires. That is
//  deliberate: "absent" has to survive parsing so the validator can say "date is
//  required" rather than silently reading a missing date as 0001-01-01. A DTO that
//  defaults its own fields moves validation into the deserializer, where it cannot
//  produce a message anyone can act on.
//
//  Unknown properties are CAPTURED rather than dropped ([JsonExtensionData]) so the
//  preview can report them. A file from a future version of the format should import
//  the parts this build understands and say plainly what it ignored: silence there
//  reads as "everything came through".
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>The whole file.</summary>
public sealed class ImportFile
{
    [JsonPropertyName("felova_import_version")]
    public int? Version { get; set; }

    /// <summary>When the generating AI produced the file. Informational only: it is
    /// shown in the preview and never used to date an entry.</summary>
    [JsonPropertyName("generated_at")]
    public string? GeneratedAt { get; set; }

    /// <summary>A free-text line describing where the notes came from ("transcribed
    /// from WhatsApp messages"). Shown in the preview so the owner can tell two files
    /// apart; never stored.</summary>
    [JsonPropertyName("source_note")]
    public string? SourceNote { get; set; }

    [JsonPropertyName("pets")]
    public List<ImportPet>? Pets { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>One pet block: which animal these entries belong to, and the entries.</summary>
public sealed class ImportPet
{
    /// <summary>"existing" or "new": see <see cref="ImportPetMatch"/>. Required, and
    /// never inferred from whether a name happens to match.</summary>
    [JsonPropertyName("match")]
    public string? Match { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Required for a new pet, optional for an existing one (where it is only
    /// ever used to disambiguate two pets sharing a name).</summary>
    [JsonPropertyName("species")]
    public string? Species { get; set; }

    // Birthday parts. Year is required for a new pet; month and day are optional and
    // are never fabricated: the app stores an unknown month/day as null rather than
    // inventing "January 1st" (see AI/domain.md → Pet birthday).

    [JsonPropertyName("birth_year")]
    public int? BirthYear { get; set; }

    [JsonPropertyName("birth_month")]
    public int? BirthMonth { get; set; }

    [JsonPropertyName("birth_day")]
    public int? BirthDay { get; set; }

    /// <summary>Condition ids for a NEW pet (see <see cref="ImportFormat.ConditionIds"/>).
    /// Ignored with a notice for an existing pet: a pile of notes is not the place to
    /// diagnose an animal that is already set up.</summary>
    [JsonPropertyName("conditions")]
    public List<string>? Conditions { get; set; }

    /// <summary>Trackers the owner defined, that this file's custom entries point at.</summary>
    [JsonPropertyName("custom_trackers")]
    public List<ImportCustomTracker>? CustomTrackers { get; set; }

    [JsonPropertyName("entries")]
    public List<ImportEntry>? Entries { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>One owner-defined tracker the file needs: matched to an existing one by
/// name, or created.</summary>
public sealed class ImportCustomTracker
{
    /// <summary>The file-local handle entries use to point here. Unique within the pet
    /// block. Never a database id: a generating AI has no way to know one, and a file
    /// carrying real ids would break the moment it was imported on another device.</summary>
    [JsonPropertyName("ref")]
    public string? Ref { get; set; }

    /// <summary>What the owner calls it. This is also the match key against trackers
    /// the pet already has.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>"tick" (it happened) or "amount" (a number in the owner's unit).</summary>
    [JsonPropertyName("shape")]
    public string? Shape { get; set; }

    /// <summary>The unit for an amount tracker ("min", "km"). Free text, never a unit
    /// system. Empty for a tick.</summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>One of the emoji the in-app picker offers. Decorative: an unknown one
    /// is normalized rather than rejected (see <see cref="ImportValidator"/>).</summary>
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }

    /// <summary>One of the five palette keys. Decorative, normalized like the icon.</summary>
    [JsonPropertyName("color")]
    public string? Color { get; set; }

    /// <summary>How often the Journal should ask. Optional: see
    /// <see cref="ImportFormat.DefaultCustomCadence"/>.</summary>
    [JsonPropertyName("cadence")]
    public string? Cadence { get; set; }

    /// <summary>Checks per day when cadence is "per_day".</summary>
    [JsonPropertyName("per_day_count")]
    public int? PerDayCount { get; set; }

    /// <summary>Whether it reaches the vet summary. Optional; defaults to the model's
    /// own default (true).</summary>
    [JsonPropertyName("include_in_report")]
    public bool? IncludeInReport { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}

/// <summary>
/// One entry, in a single flat shape covering every type.
///
/// <para><b>Why one class rather than a polymorphic hierarchy:</b> the fields are few
/// and the discriminator is a plain string, so a union would buy nothing but a custom
/// converter and a second place for the type vocabulary to live. The validator reads
/// only the fields the type calls for and reports the rest as unexpected, which is a
/// better error than a deserializer failing on a shape mismatch: "glucose entries need
/// a value" is actionable; "could not convert" is not.</para>
/// </summary>
public sealed class ImportEntry
{
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("date")]
    public string? Date { get; set; }

    /// <summary>Optional everywhere. Absent = the notes did not say when.</summary>
    [JsonPropertyName("time")]
    public string? Time { get; set; }

    // ── Per-type value fields ───────────────────────────────────────────────────

    /// <summary>weight</summary>
    [JsonPropertyName("value_kg")]
    public decimal? ValueKg { get; set; }

    /// <summary>glucose</summary>
    [JsonPropertyName("value")]
    public decimal? Value { get; set; }

    /// <summary>glucose: "before_food" / "after_food".</summary>
    [JsonPropertyName("context")]
    public string? Context { get; set; }

    /// <summary>mood, appetite_level, water_level: 1..5.</summary>
    [JsonPropertyName("level")]
    public int? Level { get; set; }

    /// <summary>appetite_amount</summary>
    [JsonPropertyName("grams")]
    public decimal? Grams { get; set; }

    /// <summary>water_amount</summary>
    [JsonPropertyName("ml")]
    public decimal? Ml { get; set; }

    /// <summary>appetite_level, appetite_amount: free-text food label.</summary>
    [JsonPropertyName("food")]
    public string? Food { get; set; }

    /// <summary>seizure: how long it lasted, in seconds. The canonical field.</summary>
    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; set; }

    /// <summary>seizure: how long it lasted, in whole minutes. <b>Legacy</b>, kept so
    /// files written before seconds existed still import; exactly equivalent to
    /// <c>duration_seconds</c> with <c>"unit": "min"</c>. Giving both is an error.</summary>
    [JsonPropertyName("duration_minutes")]
    public int? DurationMinutes { get; set; }

    /// <summary>seizure: "generalized" / "focal" / "focal_to_generalized".</summary>
    [JsonPropertyName("seizure_type")]
    public string? SeizureType { get; set; }

    /// <summary>custom: the <see cref="ImportCustomTracker.Ref"/> this records.</summary>
    [JsonPropertyName("tracker")]
    public string? Tracker { get; set; }

    /// <summary>custom: the number for an "amount" tracker.</summary>
    [JsonPropertyName("amount")]
    public decimal? Amount { get; set; }

    /// <summary>
    /// Optional, for the measured types (weight, glucose, appetite_amount, water_amount,
    /// seizure): the unit the NUMBER above is stated in, as a stable id from
    /// <c>UnitCatalog</c> ("lb", "mg_dl", "fl_oz", "oz", "min").
    ///
    /// <para><b>Omitted means the canonical unit</b>, which is what each value field's
    /// own name already says (<c>value_kg</c>, <c>ml</c>, <c>grams</c>,
    /// <c>duration_seconds</c>; glucose's <c>value</c> is mmol/L). So every file written
    /// before this field existed still means exactly what it meant.</para>
    ///
    /// <para>Felova stores the canonical value and records this as PROVENANCE, so an
    /// owner whose notes say "11.4 lb" gets a diary that says 11.4 lb back. It is never a
    /// free-text unit: an unrecognised id rejects the file rather than being quietly
    /// canonicalized, because silently reinterpreting 11.4 lb as 11.4 kg would fabricate
    /// a reading.</para>
    /// </summary>
    [JsonPropertyName("unit")]
    public string? Unit { get; set; }

    /// <summary>mood, seizure, custom: free text the owner wrote.</summary>
    [JsonPropertyName("note")]
    public string? Note { get; set; }

    /// <summary>mood: whether this note should reach the vet report's Owner's Notes.
    /// Defaults to false, matching the app: notes are private unless opted in per note.</summary>
    [JsonPropertyName("include_note_in_vet_report")]
    public bool? IncludeNoteInVetReport { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unknown { get; set; }
}
