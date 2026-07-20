namespace Animal_Diary_App.Data.Services.Journal;

/// <summary>
/// The outcome of logging something in the Journal — a warm confirmation line plus
/// the undo that reverses it. Every one-tap dose and every sheet save produces one
/// of these; the page shows a toast whose Undo button runs <see cref="UndoAsync"/>.
/// Undo is the safety net that makes one-tap medical logging safe, so it is always
/// present.
/// </summary>
public sealed record JournalSaveResult(string Message, Func<Task> UndoAsync);
