namespace Animal_Diary_App.Data.Models;

using SQLite;

// ─────────────────────────────────────────────────────────────────────────────
//  Something the owner means to ask at the next visit.
//
//  Owners forget what they meant to ask, in every consultation, without exception.
//  The thought arrives on a Tuesday evening and the appointment is three weeks
//  later; by then it is gone. So it gets written down where the owner already is,
//  in their own words, and read back to them at the visit.
//
//  A question is NOT a journal entry, and every rule about it follows from that:
//
//   • It is not a tracker, it never reaches PendingEngine, and it can never become
//     a "still to do" chip. Nothing about it is a thing to be done today.
//   • It does not appear in the timeline. The timeline is what happened to the
//     animal; this is a note to self about a conversation.
//   • It says nothing about the pet, so nothing here is ever interpreted, matched
//     on, or counted into anything. Text is the owner's words, verbatim.
//
//  NO LINK TO A VISIT, deliberately. A question is open or answered — that is the
//  whole state machine. Tying it to a visit would need a cross-table SyncId
//  reference for one bit of information nobody has asked for, and would strand
//  every question written before a visit existed to attach it to.
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>One thing to ask the vet about this pet.</summary>
public class VetQuestion : ISyncable
{
    [PrimaryKey, AutoIncrement]
    public int Id { get; set; }

    // ── Sync tracking (see ISyncable; written only via SyncStamp) ──
    [Indexed]
    public string SyncId { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; }
    public bool IsDirty { get; set; }
    public bool IsDeleted { get; set; }

    [Indexed]
    public int PetId { get; set; }

    /// <summary>The owner's words. Verbatim user text: never translated, never parsed,
    /// never fed into a sentence that assumes grammar (AI/coding-standards.md).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>When it was written down.</summary>
    public DateTime CreatedAtUtc { get; set; }

    /// <summary><b>Null = still open.</b> The entire state machine.</summary>
    public DateTime? AnsweredAtUtc { get; set; }

    /// <summary>Derived, never stored — the same posture as <c>Pet.AgeYears</c> and a
    /// vet visit being past. One column cannot disagree with itself.</summary>
    [Ignore]
    public bool IsOpen => AnsweredAtUtc is null;
}
