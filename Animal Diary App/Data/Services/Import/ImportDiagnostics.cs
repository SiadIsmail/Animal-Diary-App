namespace Animal_Diary_App.Data.Services.Import;

// ─────────────────────────────────────────────────────────────────────────────
//  What validation has to say about a file.
//
//  Two kinds, and the difference is the whole safety model:
//
//   • An ERROR rejects the ENTIRE file. Nothing is written. There is no "import the
//     good rows" path, because a file whose rows contradict each other is a file whose
//     good rows cannot be trusted either — the same transcription pass produced both.
//
//   • A NOTICE changes what will be written but not whether it happens: a row skipped
//     because the day already holds a value, a decorative field normalized, a field
//     this build does not know. Notices are the reason the preview exists — they are
//     the only place "not everything in your file will land" is ever said, and the
//     rule is that it must be said BEFORE the owner confirms, never after.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>Where in the file a diagnostic points. Mirrors the file's own structure so
/// the message can be pasted back to whatever generated it — "pets[0].entries[3]" is
/// something an AI can act on; "an entry" is not.</summary>
public readonly record struct ImportLocation(int? PetIndex, int? EntryIndex, string? Field)
{
    /// <summary>The whole file, no narrower.</summary>
    public static readonly ImportLocation File = new(null, null, null);

    public static ImportLocation Pet(int petIndex) => new(petIndex, null, null);

    public static ImportLocation PetField(int petIndex, string field) => new(petIndex, null, field);

    public static ImportLocation Entry(int petIndex, int entryIndex) => new(petIndex, entryIndex, null);

    public static ImportLocation EntryField(int petIndex, int entryIndex, string field) =>
        new(petIndex, entryIndex, field);

    public override string ToString()
    {
        if (PetIndex is null)
            return Field is null ? "file" : $"file.{Field}";

        var path = $"pets[{PetIndex}]";
        if (EntryIndex is not null)
            path += $".entries[{EntryIndex}]";
        return Field is null ? path : $"{path}.{Field}";
    }
}

/// <summary>One reason the file cannot be imported. Any single one rejects all of it.</summary>
/// <param name="Location">Where the problem is.</param>
/// <param name="Message">What is wrong, in a sentence that names the fix.</param>
public sealed record ImportError(ImportLocation Location, string Message)
{
    public override string ToString() => $"{Location}: {Message}";
}

/// <summary>Why a notice was raised. The preview groups by this, so a file with two
/// hundred already-present entries reads as one line rather than two hundred.</summary>
public enum ImportNoticeKind
{
    /// <summary>A field this build does not recognise. Ignored — see
    /// <see cref="ImportFile"/> on why unknown fields do not reject.</summary>
    UnknownField,

    /// <summary>A decorative value (icon, colour) replaced with a supported one.</summary>
    Normalized,

    /// <summary>The row will not be written because the pet already has a value in that
    /// one-per-day slot, and an import never overwrites what the owner recorded.</summary>
    SlotOccupied,

    /// <summary>The row will not be written because an identical event is already
    /// stored. This is what makes re-importing the same notes close to idempotent.</summary>
    AlreadyPresent,

    /// <summary>A value the file supplied that this build deliberately ignores in this
    /// context (conditions on an existing pet, for instance).</summary>
    Ignored
}

/// <summary>One thing the owner should know before confirming.</summary>
public sealed record ImportNotice(ImportNoticeKind Kind, ImportLocation Location, string Message)
{
    public override string ToString() => $"{Location}: {Message}";
}
