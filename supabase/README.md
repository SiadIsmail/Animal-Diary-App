# Supabase setup: Felova cloud

The app talks to Supabase project `pbwhusssrzavdgbvjtrv` (EU). These are the
one-time dashboard steps the owner runs; the SQL lives in `migrations/` and is
the source of truth for the schema.

## 1. Run the migration

Dashboard → **SQL Editor** → paste the whole of `migrations/0001_cloud_init.sql`
→ Run. Expect "Success. No rows returned". Run each migration file **once, in
number order**; never edit an already-run file: later changes get a new
numbered file.

## 2. Auth settings

Dashboard → **Authentication → Sign In / Providers**:

- **Email** provider: enabled (it is by default).
- **Confirm email**: ON.

Dashboard → **Authentication → Emails** (templates): the app verifies signup
and password recovery with the **6-digit code**, not a link (there is no
website to land on). Edit these two templates so the code is what the email
shows:

- **Confirm signup**: replace the `{{ .ConfirmationURL }}` link with:
  `Your Felova code: {{ .Token }}`
- **Reset password**: same: `Your Felova code: {{ .Token }}`

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
> this browser flow, that is only for the native one-tap picker, which we
> intentionally did not use. Just the Web client.

**Supabase dashboard**:

3. **Authentication → Sign In / Providers → Google**: enable it, paste the Web
   client's **Client ID** + **Client secret**, save.
4. **Authentication → URL Configuration → Redirect URLs**: add the app's
   deep-link `felova://auth-callback` (must match `CloudAuthService.OAuthCallback`
   and the Android `WebAuthenticationCallbackActivity` intent-filter).

That's all, no client IDs or secrets live in the app; the secret stays in
Supabase. Email+password sign-in needs none of this.

## 4. Rate limits / SMTP

The built-in email sender is limited (a few emails per hour): fine for
development. Before launch, configure custom SMTP under
**Authentication → Emails → SMTP settings** (e.g. Resend): tracked as a
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

## 6. Access codes (giveaways and comps)

Migration `0015_access_codes.sql` adds one-time codes that grant a year of full access.
A code is **given away, never sold**: selling access outside the store breaks App Store
3.1.1 and Play's Payments policy, and this path bypasses IAP by construction.

Codes are minted and read here, from the SQL editor. There is no admin screen in the app,
on purpose: minting is rare and every code should be a decision.

### Minting

```sql
-- Before the post goes up: 50 codes for Reddit, 50 for X, tagged separately.
-- Returns the codes. COPY THEM OUT of the results pane: this is the only time they are
-- handed back as a list (recovering a batch later means querying by campaign, below).
select * from public.mint_access_codes('reddit-2026-08', 50);
select * from public.mint_access_codes('x-2026-08',      50);

-- A shorter comp for testers. Same call, different length.
select * from public.mint_access_codes('beta-testers', 10, interval '3 months');

-- Codes that stop being redeemable after a while (a launch-week promo). The two durations
-- are independent: the 14 days is how long the CODE stays claimable, the year is what
-- claiming it buys, counted from the day it is claimed.
select * from public.mint_access_codes('launch-week', 200, interval '1 year',
                                       now() + interval '14 days');
```

Codes look like `FELOVA-K7M2-9XQP`, in migration 0010's alphabet (no `I`, `O`, `0`, `1`)
because they get read off a screenshot and typed by hand. They are compared upper-cased
and trimmed, so a lowercase paste works.

**Name campaigns with a date**: `reddit-2026-08`, not `reddit`. Reusing a name merges two
giveaways permanently in the stats below, and there is no way to unpick it afterwards.

### Reading the result

```sql
-- Which channel converted. codes / capacity / redeemed / redeemed_pct / first / last.
select * from public.access_code_stats order by redeemed desc;

-- The unclaimed codes of one campaign, to reissue or retire.
select code from public.access_codes where campaign = 'reddit-2026-08' and use_count = 0;

-- Support: "my code didn't work".
select r.campaign, r.code, r.redeemed_at, r.granted_until
  from public.access_code_redemptions r
  join auth.users u on u.id = r.user_id
 where u.email = 'someone@example.com'
 order by r.redeemed_at desc;

-- Retire a campaign's leftovers. This also removes the denominator, so that campaign's
-- redemption rate jumps to 100%: read the rate you care about first.
delete from public.access_codes where campaign = 'reddit-2026-08' and use_count = 0;
```

A **used** code cannot be deleted (the redemption's foreign key is `restrict`). That is
deliberate: deleting it would erase the record of who redeemed it, which is the number this
whole feature exists to produce.

### What a code is, and is not

- It sets `profiles.granted_until`, a column separate from `entitlement_active`, which
  stays writable only by the RevenueCat webhook (§5). A grant is **not** a purchase and
  must never be counted as revenue.
- Redeeming a second code **renews from that moment** rather than queueing: six months in,
  a second one-year code ends the grant a year from that day. It can never shorten a
  running grant.
- A granted owner **sponsors their caregivers** exactly like a subscriber does.
- The grant belongs to the account, so signing out gives it up on that device until the
  next sign-in. The app says so in the sign-out confirmation.
- Revoking (zeroing `granted_until`, or deleting an unused code) takes effect the next time
  that device reaches the server. An offline device keeps its cached grant until its own
  expiry date.

## 7. Creator codes (influencer attribution)

Migration `0016_creator_codes.sql`. A creator code is the opposite of an access code in
every respect, which is why they live in separate tables:

| | Access code (0015) | Creator code (0016) |
|---|---|---|
| Shape | `FELOVA-K7M2-9XQP` | `THETO` |
| Unique per person | Yes | No, everyone types the same one |
| Uses | One | Unlimited |
| Grants | A year of access | **Nothing** |
| Needs an account | Yes | No |

**Adding a creator** is one insert. The code is stored and compared upper-cased, so the
user can type `theto`, `Theto` or `THETO`.

```sql
insert into public.creator_codes (code, creator, note)
values ('THETO', 'Theto', 'YouTube, deal signed 2026-08');
```

`creator` is shown back to the user ("Thanks. We'll know you came from Theto."), so write
it the way they spell their own name.

### Their install link (Android)

Give the creator this alongside the code. Google Play preserves the `referrer` value
through the install and the app reads it on first launch, so their audience is attributed
**without typing anything**:

```
https://play.google.com/store/apps/details?id=com.felova.app&referrer=creator%3DTHETO
```

`%3D` is an encoded `=`; the whole referrer value must be URL-encoded. `utm_source=THETO`
also works as a fallback if they're pasting into a tool that builds UTM links, but
`creator=` wins when both are present.

**Android only.** Apple's campaign tokens reach App Store Connect analytics and are not
readable by the app, so on iOS the typed code is the only route. Nothing to configure,
the link just won't attribute there.

Attribution from a link is recorded as `source = 'install_referrer'`; a typed code is
`source = 'typed'`. Both are kept, so `creator_code_stats` shows which half is working.

**Reading the result:**

```sql
-- One row per creator: who arrived how, and how many purchases followed.
-- creator | code | accounts_entered | from_link | typed_in | purchases | last_purchase
select * from public.creator_code_stats order by purchases desc;

-- The individual credited purchases.
select creator, code, purchased_at, referred_at, product_id, store
  from public.creator_attributions
 order by purchased_at desc;

-- How long people took to convert, per creator.
select creator,
       count(*)                                              as purchases,
       avg(purchased_at - referred_at)                       as avg_time_to_buy
  from public.creator_attributions
 where referred_at is not null
 group by creator;

-- Retire a code without losing its history (never delete it: attributions reference it).
update public.creator_codes set active = false where code = 'THETO';
```

**Applying an attribution window later.** Every entry is a row and `referred_at` travels
onto the attribution, so a rule like "only count purchases within 30 days of entering the
code" is a `where` clause you can decide on after seeing real data, not a decision you had
to make up front:

```sql
select creator, count(*) from public.creator_attributions
 where purchased_at - referred_at < interval '30 days'
 group by creator;
```

### Coverage, honestly

Two halves, and neither is complete alone:

- **This table** only sees people with an account, because the webhook's `app_user_id` is
  a Supabase user id only after sign-in (`$RCAnonymousID` is skipped by design).
- **RevenueCat** carries the same code as subscriber attributes (`creator_code` and
  `$campaign`), set on the device when the code is entered, which covers anonymous buyers
  and shows up in RevenueCat's own charts. It dies with a reinstall.

Report **"attributed purchases"** to creators, never "your purchases". Some conversions
will always slip through: a code entered on a reinstalled app, a purchase made before
signing in, someone who found you through a creator and never typed anything.

Only the **first** purchase is credited (`INITIAL_PURCHASE` / `NON_RENEWING_PURCHASE`).
Renewals are the same sale continuing, and crediting them monthly would turn one
conversion into a recurring one.

## 8. Keys

The app embeds the project URL + publishable key (`CloudConfig` in
`Data/Services/Cloud/`). The **service-role key is never used by the app and
never committed**: it stays in the dashboard. The edge function above receives it
from Supabase's own environment; it is not stored in this repo.
