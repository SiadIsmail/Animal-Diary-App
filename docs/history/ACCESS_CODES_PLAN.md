# Access Codes — a one-time code that grants a year of full access

> Status: **BUILT (client + SQL written 2026-08-06).** This is the original design
> document, kept as the record of *why* access codes have the shape they have. It is
> **not** a description of the current implementation — for that see
> [AI/domain.md](../../AI/domain.md) (§Access codes),
> [AI/architecture.md](../../AI/architecture.md) §6,
> [AI/design-decisions.md](../../AI/design-decisions.md), and
> `supabase/README.md` §6 for the operating runbook.
>
> Shipped: migration `0015_access_codes.sql` (grant column, code/redemption/attempt
> tables, `redeem_access_code`, `my_access`, `mint_access_codes`,
> `access_code_stats`, and the `owner_has_access` clause), the `IGrantSource` seam +
> `AccessState.Granted` + every consumer named in §6,
> `Cloud/CloudAccessCodeService`, the two new `CloudErrorKind`s,
> `SignOutImpact.LosesGrant`, the Settings row + `RedeemCodeSheetView(Model)`, EN + DE
> copy, three analytics events, and seven unit tests.
>
> **Still to do by hand:** run the migration in the dashboard and walk the slice-1
> verification list (§10). Nothing in the app can do that.
>
> Scope as originally written: the owner mints codes out of band, tagged with the
> campaign they belong to (`reddit-2026-08`, `x-launch`). A signed-in user types one
> into **Settings** and gets a year of full access. A second code **renews from the
> moment it is redeemed** rather than queueing behind the first. Nothing is charged,
> nothing auto-renews, and there is nothing to cancel.
>
> Owner decisions recorded 2026-08-06 are in §12.
>
> This **extends** the gate described in MONETIZATION_PLAN §5. It does not replace
> any part of it: the trial clock, the RevenueCat entitlement and sponsorship all
> keep working exactly as they do today.

---

## 1. What this is, and what it deliberately is not

A **grant**, not a subscription. The distinction is load-bearing and every
section below depends on it:

| | Subscription (today) | Grant (this plan) |
|---|---|---|
| Who writes it | RevenueCat webhook, service role | `redeem_access_code` RPC, on the user's own row |
| Renews by itself | Yes | **Never.** Only another code renews it, by hand |
| Cancellable | Yes, in the store | Nothing to cancel |
| Ends | On lapse, detected by webhook | On a date the client already knows |
| Needs a card | Yes | No |
| Needs an account | No | **Yes** |
| Attributable | To a store, not a channel | **To the campaign that minted the code** |

Because a grant carries its own expiry date, the client needs **no offline grace
window** for it, unlike sponsorship (`BillingConfig.SponsorshipOfflineGrace`). A
cached grant expires by itself, on the device, with no server round trip. That is
the single nicest property of this design and it should not be traded away.

### The policy line (read before minting a single code)

**Give codes away. Never sell them, and never let anyone else sell them.**

Free comps (vets, testers, press, giveaways, a shelter partnership) are fine on
both stores. Selling access outside the store for digital content in the app
breaks App Store Review Guideline 3.1.1 and Google Play's Payments policy, and
this mechanism bypasses IAP by construction. If discounted *paid* access is ever
wanted, that is a store-native offer (Play promo codes / App Store offer codes),
not this feature.

Practical consequence for the UI: the redemption entry point must never sit next
to a price, must never read as a discount, and must never appear in a flow that
looks like checkout. The owner's Settings-only decision (§7) satisfies this by
construction, which is a second reason to keep it there even if discoverability
ever feels low. Verify current store policy text before launch rather than
trusting this paragraph, which will age.

## 2. Why this shape (the two rejected alternatives)

**Play promo codes / App Store offer codes.** Zero new code: RevenueCat already
carries a redemption into `profiles.entitlement_active` through the existing
webhook, and `HasFullAccess` picks it up unchanged. Rejected as the *primary*
mechanism because the redeemed thing is a real subscription (payment method on
file, auto-renews after the free period), the per-period generation caps are set
by the store rather than by us, redemption leaves the app on Android, and offer
codes need iOS, which has not shipped. Still the right answer for anything sold
at a discount, and worth revisiting when iOS lands.

**RevenueCat promotional entitlements** (granted via their REST API). Correct for
one-off comps, but keyed by app-user-id: there is nothing for a person to type.
It answers "I want to comp *this* person", not "I want a code I can print".

**A Supabase-backed code** is the near-copy of machinery this repo already ships:
migration 0010's `create_pet_invite` / `redeem_invite` are single-use codes with
row locking, expiry, attempt caps and definer-only access. This plan copies that
file's shape closely enough that it should be read alongside it.

## 3. The rule this installs

One line, in the one place MONETIZATION_PLAN §5 says the formula is allowed to
live (`EntitlementService`, and nowhere else):

```csharp
// today
HasFullAccess => _trial.IsActive || _store.HasActiveEntitlement || !_store.EntitlementKnown;

// after
HasFullAccess => _trial.IsActive
              || _store.HasActiveEntitlement
              || _grants.IsGranted
              || !_store.EntitlementKnown
              || !_grants.GrantKnown;
```

`CanEditPet`, the read-only state, the Journal chips, medication editing and
sponsorship all inherit it without a single further change. That is the whole
point of the boundary and the reason this feature is small.

Server-side the mirror is equally small, one clause in `owner_has_access`, and it
is what makes a granted owner **sponsor their caregivers** exactly like a paying
one. Without it a comped vet's assistant would still be locked out.

## 4. Server — migration 0015

Numbered, run once, never edited afterwards; a later fix is a new file whose
header states which assumption it corrects (coding-standards §Cloud).

### 4.1 The column is NOT `entitlement_active`

`profiles.granted_until timestamptz` is a **new, separate column**. Reusing the
entitlement columns would break two invariants at once:

- 0010 states the webhook is the *only* writer of `entitlement_active`. A
  client-triggered write there makes that comment false, and a patched client
  asserting its own entitlement is precisely the attack that comment prevents.
- 0011's watermark (`entitlement_event_at`) exists so out-of-order webhooks
  cannot revive stale state. A grant written outside that protocol either gets
  clobbered by the next webhook or has to fake an event time. Both are worse than
  one more column.

Keeping them apart also keeps analytics and any future revenue reporting honest:
a grant is not a sale and must never be counted as one.

### 4.2 Schema

```sql
alter table public.profiles
  add column if not exists granted_until timestamptz;

comment on column public.profiles.granted_until is
  'Full access granted by a redeemed access code, until this instant. Never a '
  'purchase: written only by redeem_access_code, never by the RevenueCat webhook, '
  'and never counted as revenue.';

-- The codes themselves. Rows are inserted by the OWNER via the SQL editor
-- (service role) or the mint helper in §4.6. There is no client insert path and
-- no admin UI, deliberately: minting is rare and every code should be deliberate.
--
-- `campaign` is the whole attribution mechanism: every code carries the channel it
-- was minted for, so "how many redeemed from Reddit vs X" is a group-by, not a
-- guess. It is a property of the CODE, never of the person who redeemed it.
create table if not exists public.access_codes (
  code         text primary key,
  campaign     text        not null,  -- 'reddit-2026-08', 'x-launch', 'vet-partners'
  grant_length interval    not null default interval '1 year',
  max_uses     int         not null default 1,
  use_count    int         not null default 0,
  expires_at   timestamptz,          -- when the CODE stops being redeemable (null = never)
  note         text,                 -- free text for the owner: who it went to, why
  created_at   timestamptz not null default now()
);

create index if not exists access_codes_campaign_idx on public.access_codes (campaign);

-- Who redeemed what. Audit trail, attribution record, and the uniqueness that
-- stops one person burning a multi-use campaign code N times.
--
-- `campaign` is COPIED here rather than read through the join, and the FK is
-- `restrict` rather than `cascade`, for the same reason: attribution has to
-- outlive the code row. Cascade would mean tidying up an old campaign's codes
-- silently erased the record of who redeemed them, which is the one number this
-- feature exists to produce. Restrict turns "delete a code someone used" into an
-- error instead of quiet data loss; the runbook only ever deletes unused codes.
create table if not exists public.access_code_redemptions (
  code        text        not null references public.access_codes(code) on delete restrict,
  campaign    text        not null,
  user_id     uuid        not null references auth.users(id) on delete cascade,
  redeemed_at timestamptz not null default now(),
  granted     interval    not null,
  granted_until timestamptz not null,   -- the resulting expiry, so renewals are legible
  primary key (code, user_id)
);

create index if not exists access_code_redemptions_campaign_idx
  on public.access_code_redemptions (campaign, redeemed_at desc);

-- Attempt log for the rate limit. `on delete cascade` from auth.users is not
-- optional: pet_invite_attempts shipped without one and needed migration 0012 to
-- fix exactly that, because it blocked account deletion.
create table if not exists public.access_code_attempts (
  id           bigserial primary key,
  user_id      uuid        not null references auth.users(id) on delete cascade,
  attempted_at timestamptz not null default now()
);
create index if not exists access_code_attempts_user_idx
  on public.access_code_attempts (user_id, attempted_at desc);

-- RLS on, no policies at all, on all three. Only the definer functions below
-- reach them; `authenticated` gets execute on those and nothing else. A user
-- must never be able to SELECT the code table and read every unused code.
alter table public.access_codes             enable row level security;
alter table public.access_code_redemptions  enable row level security;
alter table public.access_code_attempts     enable row level security;
```

### 4.3 `redeem_access_code`

```sql
create or replace function public.redeem_access_code(p_code text)
returns timestamptz
language plpgsql
security definer set search_path = ''
as $$
declare
  uid uuid;
  c   public.access_codes%rowtype;
  new_until timestamptz;
begin
  uid := (select auth.uid());
  if uid is null then
    raise exception 'redeem_access_code: no authenticated user in request context';
  end if;

  -- Same shape and window as redeem_invite. Codes are guessable in principle, so
  -- this is the only thing between a bored attacker and a free year.
  if (select count(*) from public.access_code_attempts
      where user_id = uid and attempted_at > now() - interval '15 minutes') >= 10 then
    raise exception 'redeem_access_code: too many attempts, wait a while';
  end if;
  insert into public.access_code_attempts (user_id) values (uid);

  select * into c from public.access_codes
   where code = upper(trim(p_code))
   for update;                            -- serializes two devices racing one last use

  if c.code is null
     or (c.expires_at is not null and c.expires_at < now())
     or c.use_count >= c.max_uses then
    -- Wording matters: see §4.5. Do NOT reuse redeem_invite's phrase.
    raise exception 'redeem_access_code: unknown or used access code';
  end if;

  if exists (select 1 from public.access_code_redemptions
             where code = c.code and user_id = uid) then
    raise exception 'redeem_access_code: this access code is already on your account';
  end if;

  -- RENEW from this moment, do not queue behind the existing grant (owner decision,
  -- §12.2). Redeeming a second one-year code six months in ends the grant a year
  -- from today, not eighteen months from today. The rule reads "a code always buys
  -- you a full year from the day you use it", which is the only version that can be
  -- explained in one sentence to someone in a giveaway thread.
  --
  -- greatest(...) is a guard, not the semantic: with every code the same length it
  -- never fires. It exists so that a SHORTER code (a 3-month tester comp) redeemed
  -- against a longer running grant cannot silently cut it short. Renewing must never
  -- take time away from someone.
  update public.profiles
     set granted_until = greatest(coalesce(granted_until, '-infinity'::timestamptz),
                                  now() + c.grant_length)
   where id = uid
  returning granted_until into new_until;

  if new_until is null then
    raise exception 'redeem_access_code: no profile row for this account';
  end if;

  insert into public.access_code_redemptions (code, campaign, user_id, granted, granted_until)
  values (c.code, c.campaign, uid, c.grant_length, new_until);

  update public.access_codes set use_count = use_count + 1 where code = c.code;

  return new_until;
end $$;
```

### 4.4 `my_access` and the `owner_has_access` clause

```sql
-- The caller's own billing-relevant server state. Deliberately returns ONLY the
-- grant: the trial anchor and the store entitlement are already known on-device,
-- and widening this is how a "just one more field" endpoint becomes a way to read
-- your own (then someone else's) subscription state.
create or replace function public.my_access()
returns timestamptz
language sql stable
security definer set search_path = ''
as $$
  select granted_until from public.profiles where id = (select auth.uid());
$$;

create or replace function public.owner_has_access(p_user uuid)
returns boolean
language sql stable
security definer set search_path = ''
as $$
  select coalesce(
    (select (p.entitlement_active
              and (p.entitlement_expires_at is null
                   or now() < p.entitlement_expires_at + public.entitlement_grace()))
         or (p.granted_until is not null and now() < p.granted_until)   -- ← new
         or (p.trial_started_at is not null
             and now() < p.trial_started_at + public.trial_length())
       from public.profiles p
      where p.id = p_user),
    false);
$$;

revoke execute on function public.redeem_access_code(text) from anon;
revoke execute on function public.my_access() from anon;
grant  execute on function public.redeem_access_code(text) to authenticated;
grant  execute on function public.my_access() to authenticated;
```

No `entitlement_grace()` on the grant clause. Grace exists because a renewal is
not instantaneous; a grant has no renewal, so an expiry is exactly an expiry.

### 4.5 The error-message landmine

`CloudHttp` buckets server text by substring, and the order of those checks is
significant. `redeem_invite` raises `'invalid or expired code'`, which maps to
`CloudErrorKind.InviteInvalid` and produces *sharing* copy about a pet invite. If
this function reused that phrase, a bad access code would tell the user their
**invite** was invalid.

Hence `'unknown or used access code'` above, plus a new `AccessCodeInvalid` kind
and a new `AccessCodeAlreadyUsed` kind, mapped **before** the invite checks. The
existing `too many` → `RateLimited` mapping already covers the attempt cap, so
the rate-limit message must keep that exact wording.

### 4.6 Minting a batch, and reading the result

Two functions the client never sees. They exist so a giveaway is one statement
each way, run from the dashboard SQL editor as the owner.

```sql
-- Mint N unique single-use codes for one campaign and hand them back.
-- Alphabet is 0010's: no I, O, 0 or 1, because these get read off a screenshot
-- and typed by hand. Shape: FELOVA-XXXX-XXXX.
create or replace function public.mint_access_codes(
  p_campaign text,
  p_count    int,
  p_length   interval    default interval '1 year',
  p_expires  timestamptz default null)
returns setof text
language plpgsql
security definer set search_path = ''
as $$
declare
  letters constant text := 'ABCDEFGHJKLMNPQRSTUVWXYZ';
  digits  constant text := '23456789';
  pool    constant text := letters || digits;
  new_code text;
  made int := 0;
  attempt int;
begin
  if p_count < 1 or p_count > 1000 then
    raise exception 'mint_access_codes: count must be between 1 and 1000';
  end if;

  while made < p_count loop
    attempt := 0;
    loop
      attempt := attempt + 1;
      new_code := 'FELOVA-';
      for i in 1..8 loop
        if i = 5 then new_code := new_code || '-'; end if;
        new_code := new_code || substr(pool, 1 + floor(random() * length(pool))::int, 1);
      end loop;
      begin
        insert into public.access_codes (code, campaign, grant_length, expires_at)
        values (new_code, p_campaign, p_length, p_expires);
        made := made + 1;
        return next new_code;
        exit;
      exception when unique_violation then
        if attempt >= 5 then
          raise exception 'mint_access_codes: could not generate a unique code';
        end if;
      end;
    end loop;
  end loop;
end $$;

revoke execute on function public.mint_access_codes(text, int, interval, timestamptz)
  from anon, authenticated;   -- service role / dashboard only. Never the app.

-- The attribution report. One row per campaign.
--
-- The timestamps are correlated subqueries rather than a join to the redemptions
-- table, deliberately. Joining on campaign fans every code row out by the number
-- of redemptions in that campaign, so count(*) and sum(use_count) both come back
-- multiplied: 50 codes with 10 redemptions reports 500 minted. The aggregate has
-- to be taken over access_codes alone.
--
-- `capacity` (sum of max_uses), not the code count, is the denominator of the
-- rate. They are the same number for single-use codes, and only capacity stays
-- right if a partner ever gets one code with max_uses = 50.
create or replace view public.access_code_stats as
  select c.campaign,
         count(*)                                    as codes,
         coalesce(sum(c.max_uses), 0)                as capacity,
         coalesce(sum(c.use_count), 0)               as redeemed,
         round(100.0 * coalesce(sum(c.use_count), 0)
               / nullif(sum(c.max_uses), 0), 1)      as redeemed_pct,
         (select min(r.redeemed_at) from public.access_code_redemptions r
           where r.campaign = c.campaign)            as first_redeemed,
         (select max(r.redeemed_at) from public.access_code_redemptions r
           where r.campaign = c.campaign)            as last_redeemed
    from public.access_codes c
   group by c.campaign;

revoke all on public.access_code_stats from anon, authenticated;
```

**Why unique single-use codes per campaign rather than one shared code per
channel.** A single `REDDIT2026` with `max_uses = 200` is one screenshot away
from being on a deal aggregator, and once it leaks the campaign number stops
meaning "people who came from Reddit". Unique codes cost one extra step (paste a
batch into the giveaway tooling, or DM winners individually) and keep the count
honest. `max_uses` stays in the schema for the deliberate case, such as a partner
clinic handing the same code to its own clients.

**Attribution is a live count, not a historical record.** Deleting an account
cascades its redemption rows away, so campaign totals go down when someone
erases themselves. That is the correct trade under the erasure rules in
[AI/domain.md](../../AI/domain.md); if a frozen number is ever needed, snapshot
`access_code_stats` rather than weakening the cascade.

### 4.7 Revocation

Zeroing `granted_until` (or deleting the code row and the redemption) revokes on
the next successful `my_access` refresh. A device that never comes online keeps
its cached grant until its own expiry date. Accepted: codes go to people we chose
to trust, and the alternative is a heartbeat that punishes offline users, which
this app refuses to do everywhere else.

## 5. Client — the Billing seam

### 5.1 The interface Billing declares

Mirrors `ITrialStore` / `IPetAccessSource`: **Billing declares it, Cloud
implements it, no Supabase type crosses the line.** Lives in
`Data/Services/Billing/IGrantSource.cs`.

```csharp
public interface IGrantSource
{
    /// False ONLY while a signed-in device still owes its first grant fetch.
    /// Signed out / cloud disabled must report TRUE ("nothing to wait for"), or the
    /// gate stays optimistically open forever for local-only users. Same contract,
    /// same trap, as IPetAccessSource.AccessKnown.
    bool GrantKnown { get; }

    /// True while a redeemed grant is still running. Pure and synchronous: read from
    /// a cached absolute instant, never a network call. Never throws.
    bool IsGranted { get; }

    /// When the current grant ends, for copy only. Null when there is none.
    DateTime? GrantedUntilUtc { get; }

    /// Refresh from the server if possible. Non-throwing; a failure leaves the cache.
    Task RefreshAsync();
}
```

**Why this seam owns a `RefreshAsync` when `IPetAccessSource` does not.** The
sponsorship cache rides the sync cycle, and `SyncNowAsync` returns early with
`BackupDisabled` when backup is off. A grant must reach someone who signed in
*only* to redeem a code and never turned backup on, so it cannot depend on the
sync cycle. `EntitlementService.RefreshAsync` calls `_grants.RefreshAsync()`
alongside `_store.RefreshAsync()`, which puts it on the existing launch/resume
path (`App.xaml.cs`) with no new trigger to maintain.

`NullGrantSource` (GrantKnown = true, IsGranted = false, RefreshAsync = no-op) is
registered wherever there is no cloud, exactly like `NullPetAccessSource`.

### 5.2 Where the cached expiry lives, and why that is a decision

**`SyncStateStore`, under the `cloud:` prefix** (`cloud:grantedUntil`), *not*
`AppSettings`.

`CloudSyncService` states the invariant plainly: every `cloud:`-prefixed key is
account-scoped and is wiped by one `ClearPrefixAsync` on sign-out; anything that
must survive sign-out (the trial anchor, language, preferences) belongs in
`AppSettings`. A grant belongs to the **account**, so it must not survive
sign-out. Putting it in `AppSettings` would mean redeem once, sign out, sign in as
a new account, redeem again, and keep both, on every device.

The cost is real and must be said in the UI: **signing out gives up the granted
access until you sign back in.** `SignOutImpact` already exists to make sign-out
honest about what it removes; it gains a `LosesGrant` bool and the confirm copy
gains a line. That is the correct place, and it is unit-testable.

Cache "known even when empty", the same way `KeyPetAccess` is written even when
the dictionary is empty, or a signed-in user with no grant never resolves to
known and holds the gate open.

### 5.3 The Cloud implementation

`Data/Services/Cloud/CloudAccessCodeService.cs`, implementing both
`Billing.IGrantSource` and a small cloud-facing interface for the UI:

```csharp
public interface ICloudAccessCodeService
{
    /// Redeem a code. Returns the new grant end. Throws CloudException with an
    /// AccessCode* kind the sheet localizes; raw server text only ever reaches
    /// Debug.WriteLine (coding-standards §Cloud).
    Task<DateTime> RedeemAsync(string code);
}
```

`RedeemAsync` posts `redeem_access_code`, writes the returned instant to the
cache, and raises the entitlement `StateChanged` so every open surface re-reads
the gate. Because the RPC returns the new expiry, **redemption is instantly
effective offline-after-the-fact**: no follow-up sync is required for the user to
start writing again.

DI mirrors the existing sponsorship wiring in `MauiProgram`:

```csharp
builder.Services.AddSingleton<CloudAccessCodeService>();
builder.Services.AddSingleton<ICloudAccessCodeService>(sp => sp.GetRequiredService<CloudAccessCodeService>());
builder.Services.AddSingleton<IGrantSource>(sp => sp.GetRequiredService<CloudAccessCodeService>());
```

with the `NullGrantSource` fallback on the `CloudConfig.Enabled == false` branch.

## 6. `AccessState.Granted` — and the exhaustiveness audit that must come with it

`State` is documented as "copy/telemetry only, not the gate", and a grant needs
its own value so no surface calls it a subscription. Adding the member is one
line; **finding every place that assumed the enum was closed is the actual
work.** The live consumers today, all of which must be revisited in the same
change:

| Site | Today | Risk if untouched |
|---|---|---|
| `SettingsViewModel.SubscriptionRowSubtitle` | `switch` with `_ =>` falling to the **trial** format | A granted user reads "Free trial (0 minutes left)". This is the worst one. |
| `App.MaybeShowPreEndNudgeAsync` | `!= Trial` → return | Correct by accident; a granted user gets no trial nudge, which is right. Needs its own nudge, §6.1. |
| `App.MaybeShowReadOnlyReassuranceAsync` | `!= TrialExpired` → return | See §6.1: fires with **trial** copy when a year-long grant ends. |
| `CalendarPage.MaybeHandleFirstLogAsync` | `== Trial` → show explainer | Correct: a granted user should not get the trial explainer. Confirm, don't assume. |
| `SubscribeSheetViewModel.IsSubscribed` | `== Subscribed` | A granted user is offered plans they do not need, and the sheet claims nothing about their access. |
| `NullEntitlementService.State` | `Subscribed` | Unchanged. |

Ordering in `EntitlementService.State`: `Subscribed` must outrank `Granted` (a
paying user who also holds a code is a subscriber, and the Settings row should
offer them store management), and `Granted` must outrank `Trial`.

### 6.1 The copy bug this creates, and how to avoid shipping it

`MaybeShowReadOnlyReassuranceAsync` says "your trial has ended", guarded by
`TrialEverStarted` so a caregiver who never had a trial is never told that. Most
granted users *did* start a trial once, so that guard does **not** protect them:
when a year-long grant lapses, they would be told their *trial* ended, a year
late. The same section of [AI/domain.md](../../AI/domain.md) that forbids the
caregiver case forbids this one for the same reason.

Fix in the same change: persist a `cloud:grantEverGranted` flag (or read
`granted_until is not null` in the cache) and select grant-flavoured copy when it
is set. A `GrantEndingNudgeShown` one-shot flag mirrors `PreEndNudgeShown` for the
heads-up a few days before a grant ends. Reuse `TrialMessageViewModel`'s sheet;
do not reuse its strings.

## 7. UI

One sheet, one entry point, both following the shared contract in
coding-standards §Input sheets.

- **`RedeemCodeSheetView(Model)`** on `FelovaBottomSheet`. Copy
  `SharingSheetViewModel`'s code-entry half: an uppercase-normalizing `Entry`, a
  `CanRedeem` composite bool, an inline `RedeemError` string bound through
  `StringToBoolConverter`, `IsBusy` gating. It **answers a question**, so it
  follows the awaited shape (`RedeemAsync` → result), and resolves its pending
  answer on every close path, including scrim tap and Android back.
- **Entry point: Settings, and only Settings** (owner decision, §12.3). A
  `SettingsLinkCard` sibling of the existing Subscription row in
  `SettingsPanelView.xaml`, bound to a new `SettingsVM.OpenRedeemCommand`, wearing
  its own icon and the same 42pt tinted badge as its neighbours. Nothing goes on
  the Subscribe sheet: the overwhelming majority of people who open that sheet have
  no code, and a "Have a code?" line under a price does three bad things at once.
  It advertises a discount that is not for sale (§1's policy line), it invites
  code-hunting away from the purchase, and it tells a paying customer they are
  paying more than someone else. Settings-only keeps the feature where the people
  who *were given* a code will go looking, and invisible to everyone else.
- **Where exactly in Settings.** Directly under Subscription, inside the same
  account group. The row is always present rather than conditionally hidden:
  hiding it until signed in makes it undiscoverable at precisely the moment a
  giveaway winner is hunting for it, and a permanently visible row is also one
  fewer state for the panel to get wrong.
- **Signed out** is the interesting state. Redemption requires an account, so the
  sheet's first job when `!IsSignedIn` is to say so and hand off to the existing
  account door (`CloudSheetView`, already hosted for onboarding), then return.
  Never a dead end and never a silent failure. This path is the *normal* one for a
  giveaway winner, not an edge case, so it gets tested first on a real device.
- **After success**: dismiss, and let the reassurance land as a short line stating
  the end date, not a celebration. Zero exclamation points, zero emoji.
- `x:DataType` on the new view's root; `[AcceptEmptyServiceProvider]` is not
  needed here (no new markup extension).
- The hosting page handles Android back via `BackDismiss.TryCloseTopmostOverlay`.

### 7.1 Copy rules that bite this feature specifically

Everything user-visible goes in **both** `AppStrings.resx` and `AppStrings.de.resx`
(CI enforces key parity), keys prefixed `Redeem_*`, German written natively rather
than translated ([AI/app-voice.md](../../AI/app-voice.md) §19).

- **Zero em dashes and en dashes**, anywhere in these strings (§5.1). A period.
- **Never "subscription", "premium", "unlock", "upgrade" for a grant.** It is
  access, and it ends on a date. Check the banned list in §6.1 of app-voice.
- **Never build the date into a concatenated sentence.** Full template, named
  placeholder, formatted for the current culture.
- **The success line states the new end date, never an amount of time added.**
  Renewal replaces rather than accumulates (§4.3), so "you've added a year" is
  false for anyone who redeems early, and "you now have 18 months" is false for
  everyone. One template covers both the first redemption and a renewal:
  `Redeem_Success = "You're set until {date}."`
- Errors follow §12: what happened, what to do next, no codes, no "unexpected".
  `AccessCodeInvalid` → "That code isn't valid. Check it and try again."
  `AccessCodeAlreadyUsed` → "This code is already on your account."
  `RateLimited` → "Too many tries. Give it a few minutes."
  `Network` → offline is a status line, not an error.

## 8. Analytics, and where attribution actually lives

**Campaign attribution is a SQL query, not an analytics property.** This is the
important line in the section and it is not a compromise: `access_code_stats`
(§4.6) answers "how many from Reddit, how many from X" exactly, from the
redemption rows themselves, with no sampling and no client to trust. PostHog
events are anonymous by contract and cannot be joined to an account, so putting
`campaign` on an event would produce a *worse* number than the one the database
already holds, while weakening the analytics posture to get it.

So the app sends one event, following the contract in
[AI/analytics.md](../../AI/analytics.md) (describes the event, never the user):

```
access_code_redeemed   prop: outcome ∈ { redeemed, invalid, already_used, rate_limited, offline }
```

Its only job is product friction: are people mistyping codes, hitting the rate
limit, or bouncing off the sign-in requirement? It is deliberately blind to which
campaign the code came from.

**Never properties:** the code, the campaign, the grant length, the resulting
expiry. The existing `subscribe_screen_viewed` source enum gains no new value:
the redeem sheet is not a paywall and must not be counted as one.

Worth one dashboard note when this ships: granted users look like unconverted
trial users in every RevenueCat funnel, because from RevenueCat's side they are.
`access_code_stats` is the only place they are visible as what they are.

## 9. Tests (`Animal Diary App.Tests`, plain net10.0, run by path)

The gate logic is already unit-tested (`EntitlementServiceTests`,
`SponsoredAccessTests`, `TrialServiceTests`) precisely because these seams exist.
Add a `FakeGrantSource` to `Fakes.cs` and cover:

1. Grant active → `HasFullAccess` true with trial expired and no store entitlement.
2. Grant expired → false (falls through to the existing sources).
3. `GrantKnown == false` → open, mirroring the `EntitlementKnown` case.
4. Precedence: subscription beats grant beats trial in `State`.
5. `CanEditPet` on an owned pet with only a grant → true (the grant is *your own*
   access, so it reaches your own pets, unlike sponsorship).
6. A granted **owner** sponsors caregivers: covered server-side, so assert the
   client half only (`PetAccessInfo.OwnerHasAccess` is opaque to Billing) and
   verify the SQL by hand in slice 1.
7. `SignOutImpact.RemovesAnything` true when a grant is present.

The renewal arithmetic (§4.3) lives in SQL, so the C# tests cannot reach it. It
gets its own numbered check in slice 1 instead, because it is the rule most
likely to be "fixed" later by someone who reads `greatest(...)` as a bug.

The server half is not reachable from this assembly. Verify it in the SQL editor
during slice 1 and write the queries down in `supabase/README.md` so they are
repeatable.

## 10. Slices (owner test-pause between each, per the cloud-plan rhythm)

**Slice 0 — decisions.** Done: §12, owner, 2026-08-06. Nothing blocks slice 1.

**Slice 1 — migration 0015, no client.** Run it, then verify in the SQL editor:

1. `select * from public.mint_access_codes('test-batch', 3)` returns three unique
   codes and three rows land in `access_codes` with the right campaign.
2. `select public.redeem_access_code('…')` as a test user moves `granted_until` to
   roughly one year out and writes a redemption row carrying the campaign.
3. **Renewal:** hand-set `granted_until` to six months out, redeem a second code,
   confirm the result is ~12 months from *now*, not ~18.
4. **Never shortens:** hand-set `granted_until` to two years out, redeem a
   one-year code, confirm it does not move.
5. `owner_has_access` flips true for that user, and a caregiver on their pet now
   reports `owner_access = true` from `list_my_pet_access`.
6. Re-redeeming the same code as the same user fails; a *different* user can still
   use a `max_uses > 1` code.
7. The eleventh attempt in fifteen minutes is refused.
8. `delete from access_codes` on a **used** code raises rather than cascading the
   redemption away; on an unused code it succeeds.
9. `access_code_stats` reports the batch correctly.

*Pause.*

**Slice 2 — Billing seam, no UI.** `IGrantSource`, `NullGrantSource`, the
`HasFullAccess` line, `AccessState.Granted`, every consumer in the §6 table, the
§6.1 copy split, and the tests. Desktop-verifiable end to end with a fake, which
is the point of the seam. *Pause.*

**Slice 3 — Cloud implementation.** `CloudAccessCodeService`, the `cloud:` cache,
the sign-out clear, the two new `CloudErrorKind`s and their mapping order, the
`EntitlementService.RefreshAsync` hook, DI. Still no UI: exercise it from a debug
command or a test hook. *Pause.*

**Slice 4 — the sheet.** `RedeemCodeSheetView(Model)`, the Settings row +
`OpenRedeemCommand`, the signed-out hand-off, EN + DE copy, the analytics event,
the sign-out confirm line. *Pause on a real device, walking the giveaway winner's
actual path: install, no account, Settings → Redeem → "you'll need an account" →
create one → redeem → confirm the app unlocks. Then force-close, fly offline,
confirm access holds; sign out, confirm it goes; sign back in, confirm it returns;
redeem a second code and confirm the date moves to a year out, not eighteen
months.*

**Slice 5 — docs and the minting runbook.** §11.

## 11. Documentation this must update (part of the work, not after it)

The `AI/` README states the rule: update those docs only when architecture, data
flow, a domain rule, a convention, a constraint or a design decision changes.
This feature changes four of the six, so the following are **not** optional.

| File | Change |
|---|---|
| [AI/domain.md](../../AI/domain.md) | New bullets in the access rules: a grant is a third access source; it reaches your own pets (unlike sponsorship); it needs no offline grace because it carries its own expiry; it is account-scoped and does not survive sign-out; **a second code renews from redemption and never shortens a running grant**; no surface may call a grant a subscription or tell a granted user their trial ended. |
| [AI/analytics.md](../../AI/analytics.md) | `access_code_redeemed` added to the event list with its one property, **and the rule that campaign attribution is answered in Postgres, never as an event property** (§8). The event list there is a contract, not a summary. |
| [AI/architecture.md](../../AI/architecture.md) §6 | `IGrantSource` named as the third pure seam Billing declares and Cloud implements, next to `IPetAccessSource` / `ITrialAnchor`. §5 (Cloud) gains `CloudAccessCodeService` in the folder's inventory. |
| [AI/design-decisions.md](../../AI/design-decisions.md) | Why a separate `granted_until` column rather than `entitlement_active` (§4.1); why the cache is account-scoped (§5.2); why this seam owns a refresh when `IPetAccessSource` does not (§5.1). |
| [AI/known-constraints.md](../../AI/known-constraints.md) | Revocation converges only when the device is next online (§4.7); redemption requires an account, so a local-only user cannot use a code. |
| [AI/README.md](../../AI/README.md) | Add to the non-negotiable list if and only if one of the domain rules above proves easy to re-break in review. Do not pad it. |
| [supabase/README.md](../../supabase/README.md) | **New §7, the minting runbook** (below). This is the part the owner will actually reread. |
| [AI/current-roadmap.md](../../AI/current-roadmap.md) | Nothing while this is unbuilt beyond a one-liner; remove it the moment it ships. |
| This file | Header flips to BUILT, with the shipped/deferred list, as MONETIZATION_PLAN's does. |
| `docs/history/README.md` | One row in the index table. |

### The minting runbook (draft for `supabase/README.md` §7)

**Running a giveaway is two statements.** Mint a batch tagged with the channel,
then read the result later.

```sql
-- 1. Before the post goes up: 50 codes for Reddit, 50 for X, tagged separately.
--    Returns the codes; copy them out of the results pane. They are not
--    retrievable in bulk afterwards without querying the table by campaign.
select * from public.mint_access_codes('reddit-2026-08', 50);
select * from public.mint_access_codes('x-2026-08',      50);

-- A shorter comp for testers. Same call, different length.
select * from public.mint_access_codes('beta-testers', 10, interval '3 months');

-- Codes that must not be redeemable forever (a launch week promo).
select * from public.mint_access_codes('launch-week', 200, interval '1 year',
                                       now() + interval '14 days');

-- 2. Afterwards: which channel actually converted.
select * from public.access_code_stats order by redeemed desc;

-- The unredeemed codes of one campaign, to reissue or retire.
select code from public.access_codes
 where campaign = 'reddit-2026-08' and use_count = 0;

-- One person's history (support: "my code didn't work").
select r.campaign, r.code, r.redeemed_at, r.granted_until
  from public.access_code_redemptions r
  join auth.users u on u.id = r.user_id
 where u.email = 'someone@example.com'
 order by r.redeemed_at desc;

-- Retire a campaign's leftovers. Used codes cannot be deleted (FK restrict),
-- which is deliberate: deleting them would erase the attribution.
-- Note this also drops the campaign's denominator, so its redemption rate jumps
-- to 100%. Retire leftovers only once you have read the rate you cared about.
delete from public.access_codes where campaign = 'reddit-2026-08' and use_count = 0;
```

Codes are stored and compared **upper-cased and trimmed**, so a user typing
`felova-ab3d-k7m2` works. `mint_access_codes` uses 0010's alphabet (no `I`, `O`,
`0`, `1`) because these get read off a screenshot and typed by hand.

**Campaign naming.** One string per post, dated: `reddit-2026-08`, not `reddit`.
Reusing a campaign name across two giveaways merges them permanently in
`access_code_stats`, and there is no way to unpick it afterwards.

## 12. Resolved decisions (owner, 2026-08-06)

1. **A code grants one year**, and the code carries its own length so shorter
   comps cost nothing (`grant_length`, default `interval '1 year'`).
2. **A second code renews from the moment of redemption**, it does not queue
   behind the first. Six months in, a second code ends the grant a year from that
   day, for eighteen months of use in total. Implemented in §4.3, with a guard so
   a shorter code can never cut a longer running grant short.
3. **Entry point is Settings only.** Nothing on the Subscribe sheet, so
   non-giveaway users are never shown a door that is not for them (§7).
4. **Codes are attributable.** Every code carries a `campaign`, copied onto the
   redemption row, and `access_code_stats` reports per channel (§4.2, §4.6).
   Attribution is answered in SQL, never through analytics (§8).
5. **Redemption requires a signed-in account** and is bound to it.
6. **Sign-out gives up the grant on that device** (§5.2), and the sign-out confirm
   says so. This is what stops one code covering unlimited accounts.

### Still open (not blocking; defaults chosen)

- **Redeeming while subscribed.** Default: allowed, and the grant sits behind the
  subscription so it takes over if the subscription ever lapses. The alternative
  is refusing with "you already have full access", which throws away a code the
  person legitimately holds. Change it in `redeem_access_code` if you disagree.
- **Multi-use codes.** Shipped in the schema (`max_uses`) but the runbook mints
  unique single-use codes for giveaways, per §4.6. Use the multi-use form only
  for a named partner.
- **Grant-ending nudge copy.** §6.1 says it must not reuse the trial strings. The
  exact wording is a slice-4 decision, not a design one.

## 13. Accepted limitations (record these in `known-constraints.md` when built)

- Redemption requires an account and a network connection **once**. Everything
  afterwards works offline until the grant expires.
- Revocation is not immediate for an offline device (§4.7).
- A grant does not survive sign-out on that device (§5.2), by design.
- Nothing notifies a user that their grant is ending except the app's own one-shot
  nudge; there is no email, because the app has no mailer of its own.
- The grant is invisible to RevenueCat, so RevenueCat's dashboards will keep
  showing these users as unconverted. That is correct (they did not pay) and will
  look like churn in any funnel built on RevenueCat alone.
- **Attribution is per code, not per person.** It answers "which channel did this
  redemption come from", never "what did this person do next": the redemption
  rows are in Postgres and the behaviour events are anonymous in PostHog, and
  joining them would break the analytics contract. Retention of granted users is
  measurable in aggregate, not by campaign.
- **Attribution counts shrink when accounts are deleted** (§4.6), because the
  redemption row cascades away with the user. Snapshot `access_code_stats` if a
  campaign number needs to stay fixed.
- **Minted codes are only handed back once.** `mint_access_codes` returns them
  from the call that creates them; recovering a batch afterwards means querying
  `access_codes` by campaign. Copy them somewhere before closing the results pane.
