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

## 3. Rate limits / SMTP

The built-in email sender is limited (a few emails per hour) — fine for
development. Before launch, configure custom SMTP under
**Authentication → Emails → SMTP settings** (e.g. Resend) — tracked as a
pre-launch task, nothing to do now.

## 4. Keys

The app embeds the project URL + publishable key (`CloudConfig` in
`Data/Services/Cloud/`). The **service-role key is never used by the app and
never committed** — it stays in the dashboard.
