using Android.App;
using Android.Content;
using Android.Content.PM;

namespace Animal_Diary_App;

/// <summary>
/// Receives the OAuth deep-link (felova://auth-callback) that Supabase redirects
/// to after the Google browser flow, and hands it back to MAUI's
/// <see cref="Microsoft.Maui.Authentication.WebAuthenticator"/>. The scheme here
/// must match <c>CloudAuthService.OAuthCallback</c> and Supabase's Redirect URLs
/// allow-list (see supabase/README.md).
/// </summary>
[Activity(NoHistory = true, LaunchMode = LaunchMode.SingleTop, Exported = true)]
[IntentFilter(
    new[] { Intent.ActionView },
    Categories = new[] { Intent.CategoryDefault, Intent.CategoryBrowsable },
    DataScheme = "felova",
    DataHost = "auth-callback")]
public class WebAuthenticationCallbackActivity : Microsoft.Maui.Authentication.WebAuthenticatorCallbackActivity
{
}
