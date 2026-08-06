namespace Animal_Diary_App.Helpers;

using Animal_Diary_App.Data.Services.Cloud;
using Animal_Diary_App.Data.ViewModels;

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
    /// <param name="page">The hosting page. A two-outcome confirm stays on the native
    /// dialog — both choices render as buttons there, so nothing is ambiguous.</param>
    /// <param name="impact">What leaves the device.</param>
    /// <param name="confirm">The shared confirm sheet, used when there are THREE outcomes.
    /// The native action sheet renders its extra option as plain text, which buried the
    /// "save a copy first" offer under the destructive button.</param>
    /// <param name="offerExport">Invoked if the user picks "save a copy first"; the sign-out
    /// is then abandoned (they can re-tap once the export is done). Pass null on pages that
    /// do not host the export sheet — the option is simply not offered there.</param>
    public static async Task<bool> AskAsync(
        Page page, SignOutImpact impact, ConfirmSheetViewModel? confirm, Action? offerExport)
    {
        var loc = LocalizationManager.Instance;
        var body = Describe(impact);

        // "Always let people get their data out" (app-voice §13) — the same offer the
        // pet-removal flow makes, on the same surface, for the same reason.
        if (offerExport != null && confirm != null)
        {
            var choice = await confirm.AskAsync(
                loc.GetString("Cloud_SignOutTitle"),
                body,
                new[]
                {
                    new ConfirmOption
                    {
                        Id = SaveCopyId,
                        Label = loc.GetString("Cloud_SignOutSaveCopy"),
                    },
                    new ConfirmOption
                    {
                        Id = SignOutId,
                        Label = loc.GetString("Cloud_SignOutConfirm"),
                        IsDestructive = true,
                    },
                });

            if (choice == SaveCopyId)
            {
                offerExport();
                return false;
            }
            return choice == SignOutId;
        }

        return await page.DisplayAlert(
            loc.GetString("Cloud_SignOutTitle"),
            body,
            loc.GetString("Cloud_SignOutConfirm"),
            loc.GetString("Common_Cancel"));
    }

    // Compared against instead of the localized labels, so a copy change can't silently
    // turn a chosen option into "cancelled".
    private const string SaveCopyId = "save-copy";
    private const string SignOutId = "sign-out";

    /// <summary>The message body: which pets leave, and the unrecoverable part if any.</summary>
    private static string Describe(SignOutImpact impact)
    {
        var loc = LocalizationManager.Instance;
        var parts = new List<string>();

        if (impact.PetNames.Count > 0)
            parts.Add(loc.Format("Cloud_SignOutBody", NameList(impact.PetNames)));

        // Reversible, like the pets, so it sits with them rather than with the unsynced
        // warning below. Said out loud because the alternative is someone signing out for an
        // unrelated reason and quietly losing a year they were given.
        if (impact.LosesGrant)
            parts.Add(loc.GetString("Cloud_SignOutGrant"));

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
