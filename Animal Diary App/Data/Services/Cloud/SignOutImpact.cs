namespace Animal_Diary_App.Data.Services.Cloud;

/// <summary>
/// What signing out will remove from this device, so the user is asked rather than surprised.
///
/// <para>Its own file, free of MAUI and SQLite, so the "is there anything to warn about"
/// decision can be unit-tested — same reason <c>ITrialStore</c> and <c>IPetAccessSource</c>
/// are separated out of the services that use them.</para>
/// </summary>
/// <param name="PetNames">Pets that leave the device. They are NOT deleted from the account —
/// signing back in brings them back, and the copy the user reads must say so, or a reversible
/// action reads as destruction.</param>
/// <param name="UnsyncedChanges">Rows still waiting to upload. These are the genuinely
/// unrecoverable part, which is exactly why sign-out asks rather than tells.</param>
public sealed record SignOutImpact(IReadOnlyList<string> PetNames, int UnsyncedChanges)
{
    /// <summary>Whether signing out costs anything at all. False ⇒ skip the confirm; asking
    /// when there is nothing to lose trains people to dismiss the dialog that matters.</summary>
    public bool RemovesAnything => PetNames.Count > 0 || UnsyncedChanges > 0;
}
