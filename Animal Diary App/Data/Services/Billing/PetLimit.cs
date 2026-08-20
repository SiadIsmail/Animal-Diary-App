namespace Animal_Diary_App.Data.Services.Billing;

/// <summary>
/// The one rule about how many animals the free tier covers, in one place so it cannot
/// drift between the add button, the copy, and whatever asks next.
///
/// <para>Pure and MAUI-free so it is unit-testable: the interesting cases are all
/// boundary cases (nobody, exactly one, demo pets, a lapsed subscriber with three) and
/// every one of them is a decision about somebody's animal.</para>
/// </summary>
public static class PetLimit
{
    /// <summary>How many pets the free tier covers.</summary>
    public const int FreeTierPets = 1;

    /// <summary>Whether adding ANOTHER pet is a paid action right now.
    ///
    /// <para><b>Add-time only, and it never hides a pet.</b> Someone who subscribed with
    /// three animals and has since lapsed keeps all three, fully readable <i>and fully
    /// writable</i>; this only ever answers "may a new one be created". Locking or hiding
    /// an animal to enforce a plan limit is not something this app does.</para>
    ///
    /// <para>The first pet is never blocked, so someone starting over after deleting their
    /// only pet walks straight through, and onboarding needs no special case.</para></summary>
    /// <param name="hasFullAccess">The owner's own paid access.</param>
    /// <param name="livePetCount">Pets that already exist, <b>excluding demo pets</b>. Kira
    /// and Mira are seeded by the app rather than adopted by the owner, and charging for a
    /// second pet because the app added two of its own would be indefensible.</param>
    public static bool BlocksAnotherPet(bool hasFullAccess, int livePetCount)
        => !hasFullAccess && livePetCount >= FreeTierPets;
}
