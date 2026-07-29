namespace Animal_Diary_App.Helpers;

using Animal_Diary_App.Data.Services.Cloud;

/// <summary>
/// The one place the sign-out confirmation is composed, shared by every page that hosts the
/// cloud sheet. Four hosts each hand-rolling this is how the wording drifts, and this is the
/// wording that has to be exactly right: signing out removes pets from the DEVICE, and the
/// user must be able to see from the message that signing back in brings them back.
/// </summary>
public static class SignOutPrompt
{
    /// <summary>
    /// Ask whether to sign out, given what it costs. Returns true to proceed.
    /// </summary>
    /// <param name="page">The hosting page — native confirm is the sanctioned surface for a
    /// destructive confirm; the bottom-sheet rule governs input, not confirmation.</param>
    /// <param name="impact">What leaves the device.</param>
    /// <param name="offerExport">Invoked if the user picks "save a copy first"; the sign-out
    /// is then abandoned (they can re-tap once the export is done). Pass null on pages that
    /// do not host the export sheet — the option is simply not offered there.</param>
    public static async Task<bool> AskAsync(Page page, SignOutImpact impact, Action? offerExport)
    {
        var loc = LocalizationManager.Instance;
        var body = Describe(impact);

        // "Always let people get their data out" (app-voice §13) — the same offer the
        // pet-removal flow makes, on the same surface, for the same reason.
        if (offerExport != null)
        {
            var signOut = loc.GetString("Cloud_SignOutConfirm");
            var saveCopy = loc.GetString("Cloud_SignOutSaveCopy");
            var choice = await page.DisplayActionSheet(
                loc.GetString("Cloud_SignOutTitle") + "\n\n" + body,
                loc.GetString("Common_Cancel"),
                signOut,        // destructive
                saveCopy);      // regular option, offered first

            if (choice == saveCopy)
            {
                offerExport();
                return false;
            }
            return choice == signOut;
        }

        return await page.DisplayAlert(
            loc.GetString("Cloud_SignOutTitle"),
            body,
            loc.GetString("Cloud_SignOutConfirm"),
            loc.GetString("Common_Cancel"));
    }

    /// <summary>The message body: which pets leave, and the unrecoverable part if any.</summary>
    private static string Describe(SignOutImpact impact)
    {
        var loc = LocalizationManager.Instance;
        var parts = new List<string>();

        if (impact.PetNames.Count > 0)
            parts.Add(loc.Format("Cloud_SignOutBody", NameList(impact.PetNames)));

        // Stated separately and last: everything above comes back on the next sign-in, this
        // does not. Singular has its own string rather than reading "1 changes".
        if (impact.UnsyncedChanges == 1)
            parts.Add(loc.GetString("Cloud_SignOutUnsyncedOne"));
        else if (impact.UnsyncedChanges > 1)
            parts.Add(loc.Format("Cloud_SignOutUnsynced", impact.UnsyncedChanges));

        return string.Join(" ", parts);
    }

    /// <summary>"Bella", "Bella and Max", "Bella, Max and 2 others" — names the pets the
    /// owner actually recognises rather than showing a count.</summary>
    private static string NameList(IReadOnlyList<string> names)
    {
        var loc = LocalizationManager.Instance;
        if (names.Count <= 2)
            return string.Join(", ", names);

        var shown = string.Join(", ", names.Take(2));
        var rest = names.Count - 2;
        return rest == 1
            ? loc.Format("Cloud_SignOutPetsMoreOne", shown)
            : loc.Format("Cloud_SignOutPetsMore", shown, rest);
    }
}
