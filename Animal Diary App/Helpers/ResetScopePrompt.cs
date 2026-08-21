namespace Animal_Diary_App.Helpers;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// "Delete all data" for a signed-in owner: device only, or the cloud backup too.
/// Extracted for the reason <see cref="SignOutPrompt"/> was: Today and Care each held
/// a byte-identical copy, and this is wording nobody wants to see drift.
///
/// Three outcomes, so it uses <see cref="ConfirmSheetViewModel"/> rather than the native
/// action sheet: that one renders its third option as plain text, which is where "keep my
/// backup" used to live, underneath a styled "delete everything".
/// </summary>
public static class ResetScopePrompt
{
    // Compared against instead of the localized labels, so a copy change can't silently
    // turn a chosen option into "cancelled".
    private const string DeviceOnlyId = "device-only";
    private const string EverythingId = "everything";

    public static async Task<ResetScope?> AskAsync(ConfirmSheetViewModel confirm)
    {
        var loc = LocalizationManager.Instance;

        var choice = await confirm.AskAsync(
            loc.GetString("Settings_DeleteConfirmTitle"),
            // The native action sheet had nowhere to put this, so the consequence went
            // unstated at the exact moment it mattered most.
            loc.GetString("Settings_DeleteConfirmMessage"),
            new[]
            {
                new ConfirmOption
                {
                    Id = DeviceOnlyId,
                    Label = loc.GetString("Settings_ResetDeviceOnly"),
                },
                new ConfirmOption
                {
                    Id = EverythingId,
                    Label = loc.GetString("Settings_ResetEverything"),
                    IsDestructive = true,
                },
            });

        return choice switch
        {
            DeviceOnlyId => ResetScope.DeviceOnly,
            EverythingId => ResetScope.Everything,
            _ => null,
        };
    }
}
