namespace Animal_Diary_App.Data.Services.Cloud;

/// <summary>
/// Supabase project coordinates. The publishable key is safe to embed (same
/// category as the PostHog project key — it grants nothing RLS doesn't allow);
/// the service-role key is never used by the app and never committed.
/// <see cref="Enabled"/> = false swaps in <c>NullCloudSyncService</c> and the
/// app carries zero cloud behaviour — mirrors <c>AnalyticsConfig</c>.
/// </summary>
public static class CloudConfig
{
    public const bool Enabled = true;

    public const string Url = "https://pbwhusssrzavdgbvjtrv.supabase.co";

    public const string PublishableKey = "sb_publishable_vAPqc7ARaJiI_39xTDUftA_zXWRIuXc";
}
