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

## 5. RevenueCat webhook (sponsored caregivers)

Migration `0010_sponsored_caregivers.sql` lets a pet's **owner** cover everyone caring
for that pet, so caregivers do not each need a subscription. It needs one server-side
fact the app cannot be trusted to assert: is this owner actually subscribed?

`supabase/functions/revenuecat-webhook/` is the **only** writer of
`profiles.entitlement_active`.

```bash
supabase secrets set REVENUECAT_WEBHOOK_SECRET=<a long random string>
supabase functions deploy revenuecat-webhook --no-verify-jwt
```

`--no-verify-jwt` is required: RevenueCat sends its own shared secret, not a Supabase
JWT. That secret check is therefore the only thing standing between this and an open
write endpoint.

Then RevenueCat Dashboard → **Integrations → Webhooks**:

- **URL:** `https://<project>.supabase.co/functions/v1/revenuecat-webhook`
- **Authorization header:** the same value as `REVENUECAT_WEBHOOK_SECRET`.

Also check **Project settings → transfer behavior**. The app now calls RevenueCat
`Login` when someone signs in, which aliases their anonymous purchase onto their
account. If transfer behavior keeps purchases with the original id, a customer whoopenssl rand -hex 32
bought *before* making an account loses their subscription the moment they make one.
**Verify this in sandbox** — the failure is silent and hits paying users.

The trial half needs no configuration: the app claims its own anchor through
`claim_trial_anchor` on the first sync after signing in.

## 6. Keys

The app embeds the project URL + publishable key (`CloudConfig` in
`Data/Services/Cloud/`). The **service-role key is never used by the app and
never committed** — it stays in the dashboard. The edge function above receives it
from Supabase's own environment; it is not stored in this repo.
