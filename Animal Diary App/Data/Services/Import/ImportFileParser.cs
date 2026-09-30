namespace Animal_Diary_App.Data.Services.Import;

using System.Text.Json;

/// <summary>
/// Text on disk to <see cref="ImportFile"/>. Nothing but deserialization: every
/// judgement about whether the contents make sense belongs to
/// <see cref="ImportValidator"/>, so a malformed file and an invalid one produce the
/// same kind of message and reach the same screen.
///
/// <para>The tolerances below exist because of where these files come from: a chat
/// window. They forgive presentation, never data: a trailing comma is a formatting
/// artefact, whereas a missing date is a fact nobody has.</para>
/// </summary>
public static class ImportFileParser
{
    private static readonly JsonSerializerOptions Options = new()
    {
        // An AI asked for "date" may write "Date". Casing is presentation; rejecting it
        // would fail a file whose data is entirely correct.
        PropertyNameCaseInsensitive = true,

        // Both are routine in hand-edited and model-generated JSON, and neither can
        // change what a value means.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>Parse, or return the one error that stopped it. Returns a single error
    /// rather than a list because JSON parsing halts at the first problem: there is no
    /// second syntax error to report until the first is fixed.</summary>
    public static bool TryParse(string text, out ImportFile file, out ImportError error)
    {
        file = new ImportFile();
        error = null!;

        if (string.IsNullOrWhiteSpace(text))
        {
            error = new ImportError(ImportLocation.File, "The file is empty.");
            return false;
        }

        var json = StripCodeFence(text);

        try
        {
            var parsed = JsonSerializer.Deserialize<ImportFile>(json, Options);
            if (parsed is null)
            {
                error = new ImportError(ImportLocation.File, "The file did not contain a Felova import object.");
                return false;
            }

            file = parsed;
            return true;
        }
        catch (JsonException ex)
        {
            // The line/position in a JsonException is genuinely useful here: the file
            // was machine-generated, so "line 84" points at a specific entry the owner
            // can hand back to whatever wrote it. The raw message is appended for the
            // same reason; there is no user data in it worth hiding, and this whole
            // surface is developer-facing.
            var where = ex.LineNumber is long line
                ? $" (line {line + 1})"
                : string.Empty;
            error = new ImportError(ImportLocation.File, $"The file is not valid JSON{where}: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Drop a surrounding markdown code fence.
    ///
    /// <para>These files are copied out of a chat window, where JSON almost always
    /// arrives wrapped in ```json … ```. Leaving it in produces "invalid JSON at line 1",
    /// which sends the owner back to the AI to fix a file that was already correct.
    /// Cheap, deterministic, and it cannot alter a byte of the data: a fence is only
    /// stripped when the text both opens and closes with one.</para>
    /// </summary>
    private static string StripCodeFence(string text)
    {
        var trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) ||
            !trimmed.EndsWith("```", StringComparison.Ordinal) ||
            trimmed.Length < 6)
        {
            return trimmed;
        }

        // Drop the opening fence and its optional language tag (```json), then the
        // closing one.
        var firstBreak = trimmed.IndexOf('\n');
        if (firstBreak < 0)
            return trimmed;

        var body = trimmed[(firstBreak + 1)..];
        var lastFence = body.LastIndexOf("```", StringComparison.Ordinal);
        return lastFence < 0 ? body.Trim() : body[..lastFence].Trim();
    }
}
