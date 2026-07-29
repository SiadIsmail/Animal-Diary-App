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

### Transfer behaviour must be "keep with original App User ID"

RevenueCat Dashboard → **Project settings → transfer behavior** (wording varies by
dashboard version; it is the setting about what happens when a store account's purchase
is claimed by a second App User ID).

**Felova needs "keep with original", not "transfer to new".** A Play/App Store
subscription belongs to the *store* account, not the Felova account, so with transfer
enabled anyone who signs into a second Felova account on the same phone inherits the
first one's subscription. Access decides who may **sponsor caregivers**, so one purchase
would mint several sponsoring accounts.

What still works with "keep with original":

- **Anonymous → identified** is an alias/merge, not a transfer, so someone who paid before
  ever making an account keeps their subscription when they create one.
- **The same account on a second device** restores normally.

What changes: a genuine account migration (moving to a new email) no longer carries the
subscription across. At this scale, handle those by hand.

The app surfaces the resulting state rather than failing: a purchase or restore blocked
because the store account's subscription belongs to another Felova account returns
`PurchaseOutcome.OwnedByAnotherAccount` and says so, with the two real ways out (sign in
as that account, or manage it in the store).

The trial half needs no configuration: the app claims its own anchor through
`claim_trial_anchor` on the first sync after signing in.

## 6. Keys

The app embeds the project URL + publishable key (`CloudConfig` in
`Data/Services/Cloud/`). The **service-role key is never used by the app and
never committed** — it stays in the dashboard. The edge function above receives it
from Supabase's own environment; it is not stored in this repo.
