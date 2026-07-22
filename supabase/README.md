# Supabase setup — Felova cloud

The app talks to Supabase project `pbwhusssrzavdgbvjtrv` (EU). These are the
one-time dashboard steps the owner runs; the SQL lives in `migrations/` and is
the source of truth for the schema.

## 1. Run the migration

Dashboard → **SQL Editor** → paste the whole of `migrations/0001_cloud_init.sql`
→ Run. Expect "Success. No rows returned". Run each migration file **once, in
number order**; never edit an already-run file — later changes get a new
numbered file.

## 2. Auth settings

Dashboard → **Authentication → Sign In / Providers**:

- **Email** provider: enabled (it is by default).
- **Confirm email**: ON.

Dashboard → **Authentication → Emails** (templates): the app verifies signup
and password recovery with the **6-digit code**, not a link (there is no
website to land on). Edit these two templates so the code is what the email
shows:

- **Confirm signup** — replace the `{{ .ConfirmationURL }}` link with:
  `Your Felova code: {{ .Token }}`
- **Reset password** — same: `Your Felova code: {{ .Token }}`

## 3. Google Sign-In (Android)

The app offers "Continue with Google" on Android via the system browser
(MAUI `WebAuthenticator` → Supabase OAuth with PKCE). Setup, once:

**Google Cloud Console** (console.cloud.google.com):

1. Create/select a project → **APIs & Services → OAuth consent screen**: set it
   up (External), add your email as a test user while unpublished.
2. **APIs & Services → Credentials → Create credentials → OAuth client ID →
   Web application.** Under **Authorized redirect URIs** add Supabase's callback:
   `https://pbwhusssrzavdgbvjtrv.supabase.co/auth/v1/callback`.
   Copy the **Client ID** and **Client secret**.

> A separate *Android* OAuth client (with the app's SHA-1) is **not** needed for
> this browser flow — that is only for the native one-tap picker, which we
> intentionally did not use. Just the Web client.

**Supabase dashboard**:

3. **Authentication → Sign In / Providers → Google**: enable it, paste the Web
   client's **Client ID** + **Client secret**, save.
4. **Authentication → URL Configuration → Redirect URLs**: add the app's
   deep-link `felova://auth-callback` (must match `CloudAuthService.OAuthCallback`
   and the Android `WebAuthenticationCallbackActivity` intent-filter).

That's all — no client IDs or secrets live in the app; the secret stays in
Supabase. Email+password sign-in needs none of this.

## 4. Rate limits / SMTP

The built-in email sender is limited (a few emails per hour) — fine for
development. Before launch, configure custom SMTP under
**Authentication → Emails → SMTP settings** (e.g. Resend) — tracked as a
pre-launch task, nothing to do now.

## 5. Keys

The app embeds the project URL + publishable key (`CloudConfig` in
`Data/Services/Cloud/`). The **service-role key is never used by the app and
never committed** — it stays in the dashboard.
