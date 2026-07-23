namespace Animal_Diary_App.Data.Services.Cloud;

using System.Diagnostics;

/// <summary>
/// A tiny in-memory ring buffer of recent cloud events, so the hidden developer
/// panel can surface the failures that are otherwise swallowed to
/// <see cref="Debug"/> (a dropped Google sign-in, a silent session expiry, a
/// sync error). <see cref="Record"/> is the single sink — it also mirrors to
/// <see cref="Debug"/>, so existing debug-console visibility is unchanged.
///
/// Deliberately holds only coarse technical detail (event, HTTP status, trimmed
/// server message). It must never store credentials: access/refresh tokens live
/// in headers and are never passed here, and bodies are trimmed. Cleared on
/// data reset via <see cref="Clear"/>.
/// </summary>
public static class CloudDiagnostics
{
    private const int MaxEntries = 120;
    private static readonly object Gate = new();
    private static readonly LinkedList<string> Entries = new();

    public static void Record(string message)
    {
        var line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (Gate)
        {
            Entries.AddLast(line);
            while (Entries.Count > MaxEntries)
                Entries.RemoveFirst();
        }
        Debug.WriteLine(message);
    }

    /// <summary>Newest-first snapshot for display.</summary>
    public static IReadOnlyList<string> Snapshot()
    {
        lock (Gate)
            return Entries.Reverse().ToList();
    }

    public static void Clear()
    {
        lock (Gate)
            Entries.Clear();
    }
}
