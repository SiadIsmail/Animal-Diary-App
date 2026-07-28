# Billing System Audit — RevenueCat / Trial / Read-only Gate

> Date: 2026-07-28. Scope: the whole monetization stack — `Data/Services/Billing/`,
> the subscribe + trial-message VMs/views, the read-only gate wiring, lifecycle hooks,
> DI, config/secret, and analytics. Method: read every billing file against current
> official RevenueCat / Play Billing docs. Severity is judged for **production +
> long-term maintainability**, not "does it work today."

## How to read this

Each finding: **best practice → what we do → gap → impact → fix → location**. Severity:
Critical (blocks a safe production ship), High (real user-facing correctness/UX or
maintainability risk), Medium, Low.

Counts: **2 Critical · 6 High · 7 Medium · 6 Low.**

---

## Executive summary

The architecture is genuinely good: one boundary (`IEntitlementService`), one gate
(`HasFullAccess`), the trial source isolated behind `TrialService` for reversibility, a
per-platform DI swap, and RevenueCat types confined to a single file. That foundation is
production-grade and matches the app's house patterns.

The risks are almost all at the **edges**: (1) two release-blocking config values are
live test values in source, (2) a cold-launch race can briefly lock a paying user, (3) a
cross-thread list can throw, (4) the store can't push mid-session subscription changes to
us, (5) pending/already-owned purchases are mis-modelled as failures, and (6) there are
zero automated tests around logic that is otherwise perfectly testable.

---

## Resolution log (2026-07-28)

Owner asked to fix everything **except C1 and C2** (left as release-checklist items).
Implemented and verified — app builds clean (Windows, 0 errors) and **17 billing unit
tests pass** (`Animal Diary App.Tests`, `dotnet test`).

| Finding | Status | What changed |
|---|---|---|
| **C1** Test Store key shippable | ⏸️ **Left (owner)** | still `Enabled=true` + `test_` key — release-checklist item |
| **C2** 2-minute trial | ⏸️ **Left (owner)** | still 2 min — release-checklist item |
| **H1** cold-launch lock race | ✅ Fixed | `IStoreBilling.EntitlementKnown`; `HasFullAccess` optimistic-until-known; `AccessState.Unknown` now used |
| **H2** live `Offers` list | ✅ Fixed | immutable snapshot swap (`_offers`/`_packages` volatile, `ToArray`) |
| **H3** no update listener | ⚠️ Mitigated | binding can't expose it; documented + resume refresh retained |
| **H4** pending/delayed = failure | ✅ Fixed | `PurchaseOutcome.Pending`; success-but-inactive → Pending (never "failed"); pending UI copy |
| **H5** no tests | ✅ Fixed | `ITrialStore` seam + net9.0 test project, 17 tests (trial math, gate truth table, H1, purchase/restore, Null) |
| **H6** porous read-only | ✅ Fixed | medication add/edit, pet-profile edit, condition setup gated; subscribe sheet hosted on Manage/Medications |
| **M1** already-owned = error | ✅ Fixed | `ResolveAlreadyOwnedAsync` reconciles to the entitlement |
| **M2** entitlement fallback silent | ✅ Fixed | fallback now logs a warning (rename to a spaceless id is a dashboard task) |
| **M3** hardcoded manage URL | ✅ Fixed | `GetManagementUrlAsync` via the SDK, generic Play URL fallback |
| **M4** no failure telemetry | ✅ Fixed | `purchase_failed` / `restore_failed` / `offers_load_failed` with coarse reason |
| **M5** no timeouts | ✅ Fixed | `WithTimeout` bounds every background store call (15s) |
| **M6** caregiver billing undefined | 📝 Documented (below) | each device trials/subscribes independently — a product decision, not a bug |
| **M7** double-configure | ✅ Fixed | cached init `Task` (configure-once) |
| **L1** clock-trustful trial | ✅ Noted | comment on `TrialService`; clock now injectable |
| **L2** no backup agent | ⏸️ Left | low impact (subscriptions restore via the store account) |
| **L3** pre-end nudge unwired | ✅ Fixed | wired with real dose count + weeks tracked, once, a few days before end |
| **L4** no locked affordance | ⏸️ Left | deliberate non-naggy stance — revisit if data shows confusion |
| **L5** restore offline undistinguished | ✅ Fixed | offline vs generic on restore, like offers |
| **L6** buttons not dimmed mid-purchase | ✅ Fixed | offer list `IsEnabled` bound to `CanInteract` |

### M6 — caregiver billing (product decision, documented)
Each device runs its own trial + entitlement: RevenueCat uses a per-install anonymous
id, not linked to the Supabase account, and subscriptions are per-store-account. So a
caregiver who joins a shared pet on their own phone gets **their own** 14-day trial and,
after that, **their own** paywall — the owner's subscription does not extend to them.
That may be the intended model (each user pays) or undesirable (owner covers caregivers).
Making the owner's subscription cover caregivers would require **server-side entitlement
linking** keyed off the Supabase account (out of RevenueCat's default model) — a real
design task, not a code tweak. **Current behavior: independent per-user billing.**

## Architecture

| Aspect | Assessment |
|---|---|
| Separation of concerns | **Strong.** `IEntitlementService` (gate) ⟶ `TrialService` (trial) + `IStoreBilling` (store) ⟶ `RevenueCatStoreBilling` (SDK). No RevenueCat type escapes the last file. |
| Reversibility | **Strong.** Trial source is one service; switching to a store-native trial is a contained change, exactly as designed. |
| DI / environment | **Good.** `#if ANDROID || IOS` + `BillingConfig.Enabled` → real vs `Null*`; Windows/macOS dev never locks. |
| State management | **Adequate.** `_hasEntitlement` bool + computed trial → `State`/`HasFullAccess`. See H1 (no "unknown/loading" state) and H2 (list races). |
| Maintainability | **Good**, held back by **zero tests** (H5) and a couple of pragmatic hacks that mask config errors (M2). |

---

## Critical

### C1 — Test Store key + `Enabled=true` can ship to production
- **Best practice:** *"Never submit apps using Test Store API keys to production. Always switch to platform-specific keys for release builds; automate key switching via build configuration."*
- **We do:** `BillingConfig.Enabled = true` (const) with a `test_…` Test Store key hardcoded in `BillingConfig.Secret.cs`. Nothing ties the key or `Enabled` to Debug vs Release.
- **Impact:** if a release build goes out as-is, **every real user gets simulated purchases and pays nothing** — zero revenue, and "subscriptions" that don't exist on Google Play. This is the single highest-risk item.
- **Fix:** make the key build-config-driven — `goog_…`/`appl_…` for `Release`, `test_…` only in `Debug` (e.g. `#if DEBUG` in `ApplySecrets`, or two fields chosen by `#if DEBUG`). Add a startup assertion that refuses to run a Release build with a `test_`-prefixed key. Add a release checklist line.
- **Location:** [BillingConfig.cs](Animal%20Diary%20App/Data/Services/Billing/BillingConfig.cs), `BillingConfig.Secret.cs`.

### C2 — `TrialLength = 2 minutes` is committed
- **We do:** `public static readonly TimeSpan TrialLength = TimeSpan.FromMinutes(2);` — a testing value.
- **Impact:** shipping this locks every user 2 minutes after onboarding. Also makes `PreEndNudgeDaysBefore = 3` (days) nonsensical relative to a 2-minute trial.
- **Fix:** restore `TimeSpan.FromDays(14)` before any release; ideally `#if DEBUG` a short value so it can't be forgotten. Add to release checklist.
- **Location:** [BillingConfig.cs:27](Animal%20Diary%20App/Data/Services/Billing/BillingConfig.cs#L27).

---

## High

### H1 — Cold-launch race can briefly lock (or falsely "expire") a paying subscriber
- **Best practice:** gate on a *known* entitlement; treat the pre-load window as "not yet determined," not as "no access."
- **We do:** `State`/`HasFullAccess` derive from `_store.HasActiveEntitlement`, which is `false` until `InitializeAsync` finishes `GetCustomerInfo`. During that sub-second window, a subscriber whose trial has expired computes as `TrialExpired` / `HasFullAccess == false`. `AccessState.Unknown` exists but is never returned.
- **Impact:** on a slow/cold launch a **paying user can momentarily be gated** if they tap an add action before init completes. (The read-only *reassurance* is safe — it runs after `InitializeAsync` — but the gate itself reads live.)
- **Fix:** add a `HasCheckedEntitlement` flag in `RevenueCatStoreBilling` (set once `GetCustomerInfo` first returns). While unknown, have `EntitlementService.HasFullAccess` return **optimistic true** (a genuinely-expired user gets ~1s of grace — harmless) or return `AccessState.Unknown` and have the gate not lock on Unknown. RevenueCat's on-disk cache usually makes this fast, but the window is real.
- **Location:** [EntitlementService.cs](Animal%20Diary%20App/Data/Services/Billing/EntitlementService.cs), [RevenueCatStoreBilling.cs:35-58](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L35-L58).

### H2 — `Offers` exposes a live list mutated on a background thread
- **Best practice:** never hand out a collection that another thread mutates.
- **We do:** `public IReadOnlyList<SubscriptionOffer> Offers => _offers;` returns the live `List<>`. `LoadOfferingsAsync` does `_offers.Clear()`/`Add` on a background continuation; `SubscribeSheetViewModel.RefreshMode` enumerates `_entitlements.Offers` on the UI thread. The `_offersGate` semaphore serialises *writers* but not reader-vs-writer.
- **Impact:** intermittent `InvalidOperationException: Collection was modified` / torn reads when the sheet opens exactly as a refresh lands — a crash on the paywall.
- **Fix:** return a snapshot — `Offers => _offers.ToArray();` (or build a fresh immutable list under the gate and swap the reference).
- **Location:** [RevenueCatStoreBilling.cs:36](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L36), [:155-184](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L155-L184); [SubscribeSheetViewModel.cs:170-173](Animal%20Diary%20App/Data/ViewModels/SubscribeSheetViewModel.cs#L170-L173).

### H3 — No `CustomerInfo` update listener → mid-session subscription changes are missed
- **Best practice:** register the `UpdatedCustomerInfoListener` (Android) / delegate (iOS); it fires whenever CustomerInfo changes on-device, so expiry, renewal, billing-issue/grace, and refund/revocation reflect in real time.
- **We do:** we only pull `GetCustomerInfo` on init, app resume, purchase, restore, and offers-refresh. The **Kebechet binding does not expose the update listener**, so we can't subscribe even if we wanted to.
- **Impact:** a subscription that expires or is refunded **while the app is open** keeps access until the next resume/relaunch. Acceptable for a calm app, but it's a real gap vs best practice, and it's a *binding limitation* worth recording.
- **Fix:** (a) document the limitation; (b) add a lightweight periodic/foreground `RefreshAsync` (we already refresh on resume — consider also on tab focus); (c) longer term, evaluate a binding that surfaces the listener, or contribute it upstream.
- **Location:** [RevenueCatStoreBilling.cs](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs); resume hook in [App.xaml.cs](Animal%20Diary%20App/App.xaml.cs) `OnResume`.

### H4 — Pending purchases (and delayed entitlements) are modelled as failures
- **Best practice:** `PAYMENT_PENDING` (deferred payment, family approval, slow card) is **not** a failure — the purchase may complete later; tell the user it's pending and it'll unlock when approved.
- **We do:** any non-cancel, non-success maps to `PurchaseOutcome.Failed` → generic *"That didn't go through."* And a *successful* transaction whose entitlement isn't yet active is also returned as `Failed` (a deliberate config-error surfacing, but it collides with legitimate propagation delay).
- **Impact:** a user whose payment is pending is told it failed and may retry → confusion, possible duplicate attempts; a payer on a slow entitlement propagation sees a false failure.
- **Fix:** add `PurchaseOutcome.Pending`; map `PaymentPendingError` to it with a "we'll unlock it once it's approved" message and no retry pressure. Separate "genuine store failure" from "succeeded but entitlement not yet active" (log the latter as a config warning but don't tell the user it failed if the transaction really succeeded).
- **Location:** [RevenueCatStoreBilling.cs:98-124](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L98-L124); [IEntitlementService.cs](Animal%20Diary%20App/Data/Services/Billing/IEntitlementService.cs) (`PurchaseOutcome`); [SubscribeSheetViewModel.cs:201-224](Animal%20Diary%20App/Data/ViewModels/SubscribeSheetViewModel.cs#L201-L224).

### H5 — Zero automated tests
- **Best practice:** the pure logic (trial math, gate composition, apply/refresh, plan mapping) is exactly what should be unit-tested; the plan itself called for a testable, MAUI-free engine.
- **We do:** no test project exists in the repo at all.
- **Impact:** every future change to trial/gate logic is unverified; regressions (e.g. an off-by-one in `DaysLeft`, a broken `HasFullAccess`) ship silently. This is the biggest long-term-maintainability gap.
- **Fix:** add a test project. `TrialService` (inject a fake clock — see note), `EntitlementService` (fake `IStoreBilling` + `TrialService`), and `SubscribeSheetViewModel` (fake `IEntitlementService`) are all trivially testable. Cover the matrix in the Testing section below. Note: `TrialService` currently reads `DateTime.UtcNow` directly — inject an `ITimeProvider`/`Func<DateTime>` to make expiry testable.
- **Location:** whole `Data/Services/Billing/`.

### H6 — "Read-only" is porous: medication / pet-edit / condition gates are missing
- **Best practice:** a read-only state should actually be read-only for "adding more."
- **We do:** gates exist on Journal logging, extra-pet add, and caregiver invite. **Not** on: add/edit medication + schedule, edit pet profile, condition/care-plan setup (those pages don't host the subscribe sheet).
- **Impact:** a locked user can still create/edit medications and edit pets via the Manage/Medications pages — the read-only promise leaks.
- **Fix:** host the subscribe sheet on `ManagePetPage`/`MedicationsPage` and gate their add/edit triggers (same pattern as `PetsPage.OnAddPetClicked`). Tracked in `MONETIZATION_PLAN.md`.
- **Location:** `ManagePetPage.xaml.cs`, `MedicationsPage.xaml.cs`, `MedicationViewModel`.

---

## Medium

### M1 — `PRODUCT_ALREADY_PURCHASED` treated as a generic failure
- **Best practice:** already-owned → resolve to the existing entitlement (effectively a restore/success), not an error.
- **We do:** `ProductAlreadyPurchasedError` falls into `Failed` → *"didn't go through."*
- **Impact:** a user who already subscribed (e.g. reinstalled, or bought on another device) gets a confusing error instead of being unlocked.
- **Fix:** on `ProductAlreadyPurchased`, call `RefreshEntitlementAsync` and return `Success` if the entitlement is now active (or route to Restore).
- **Location:** [RevenueCatStoreBilling.cs:122-124](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L122-L124).

### M2 — Entitlement id `"Felova Full"` + "any active entitlement" fallback masks misconfig
- **Best practice:** entitlement identifiers are code identifiers (conventionally no spaces), matched exactly.
- **We do:** `EntitlementId = "Felova Full"`, and `IsPremiumActive` falls back to "any active entitlement." The fallback saved a paying user once, but it means a future *second* entitlement would wrongly grant full access, and it hides dashboard mistakes.
- **Impact:** correct today for a single-tier app; a latent over-grant + reduced diagnosability if the entitlement model ever grows.
- **Fix:** rename the dashboard entitlement to a spaceless id (e.g. `felova_full`), update `EntitlementId`, and keep the fallback but log a warning when it fires (so a mismatch is visible, not silent).
- **Location:** [BillingConfig.cs](Animal%20Diary%20App/Data/Services/Billing/BillingConfig.cs), [RevenueCatStoreBilling.cs:199-206](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L199-L206).

### M3 — "Change or cancel" hardcodes the Play URL
- **Best practice:** use the SDK's `GetManagementSubscriptionUrl()` — it returns the correct per-platform URL (and deep-links to the specific subscription).
- **We do:** `Launcher.OpenAsync("https://play.google.com/store/account/subscriptions")` — Android-only and generic.
- **Impact:** wrong destination on iOS when it ships; less precise on Android.
- **Fix:** expose `GetManagementSubscriptionUrl()` through the boundary and open that; fall back to the generic URL if null.
- **Location:** [SubscribeSheetViewModel.cs:186-190](Animal%20Diary%20App/Data/ViewModels/SubscribeSheetViewModel.cs#L186-L190).

### M4 — No production telemetry on purchase/restore failures
- **Best practice:** monitor conversion + failure reasons (coarsely, no PII).
- **We do:** failures are `Debug.WriteLine` only (stripped in Release). PostHog is available and already used.
- **Impact:** in production you're blind to *why* purchases fail (offline? cancelled? store error? config?), which is exactly what you need at 10→N users.
- **Fix:** add anonymous events — `purchase_failed { reason }`, `restore_failed { reason }`, `offers_load_failed { reason }` — reason being a coarse enum bucket, never a message.
- **Location:** [SubscribeSheetViewModel.cs](Animal%20Diary%20App/Data/ViewModels/SubscribeSheetViewModel.cs); event constants in `AnalyticsEvents`.

### M5 — No timeout / cancellation on RevenueCat calls
- **We do:** `GetOfferings`/`GetCustomerInfo`/`PurchaseProduct` are awaited with no `CancellationToken`; the sheet's loading spinner has no upper bound.
- **Impact:** on a stalled connection the spinner can hang indefinitely (the SDK has internal timeouts, but we don't guarantee UI recovery).
- **Fix:** pass a `CancellationToken` with a sane timeout to the offerings/customer-info fetches; on timeout, fall through to the offline/problem message.
- **Location:** [RevenueCatStoreBilling.cs](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs).

### M6 — Caregiver billing model is undefined
- **We do:** each device runs its own trial + entitlement (RevenueCat anonymous id per install; not linked to the Supabase account). A caregiver who joins a shared pet on their own phone/Play account gets their own 14-day trial, then their own paywall.
- **Impact:** unclear product behavior — does the owner's subscription cover caregivers, or must each pay? Today: each pays independently. May be fine, may be surprising.
- **Fix:** decide the policy. If caregivers should ride the owner's subscription, that needs server-side entitlement linking (out of RevenueCat's default per-store-account model) — a real design task, not a code tweak. At minimum, document the current behavior.
- **Location:** cross-cutting (`CloudSharingService`, billing).

### M7 — Possible double-`configure` on a fast sheet-open during init
- **We do:** `_initialized` is set `true` *before* `LoadOfferings`/`GetCustomerInfo` complete; `RefreshOffersAsync` re-enters `InitializeAsync` when `!_initialized`. During the `Initialize()` await window a concurrent path can call `_rc.Initialize` twice.
- **Impact:** RevenueCat logs a "configured twice" warning; generally harmless but untidy and a latent source of confusion.
- **Fix:** guard init with a cached `Task` (init-once), so concurrent callers await the same operation.
- **Location:** [RevenueCatStoreBilling.cs:40-88](Animal%20Diary%20App/Data/Services/Billing/RevenueCatStoreBilling.cs#L40-L88).

---

## Low

- **L1 — Trial anchor is local & clock-trustful.** `TrialStartUtc` in SQLite; `IsActive` compares `DateTime.UtcNow`. Reinstall resets it (accepted decision) and setting the device clock back extends it. Self-defeating for a serious owner; fine to accept, worth a comment. Harden later via the Supabase `profiles` row if data justifies it. — [TrialService.cs](Animal%20Diary%20App/Data/Services/Billing/TrialService.cs).
- **L2 — Android Auto Backup / `RevenueCatBackupAgent` not configured.** The anonymous RevenueCat id isn't preserved across reinstall/device change; subscriptions still restore via the Play account, so impact is low. — AndroidManifest.
- **L3 — Pre-end nudge unimplemented; `PreEndNudgeDaysBefore` is dead config; `days_active_in_trial` event never emitted.** Plan features not yet wired. — `MONETIZATION_PLAN.md`.
- **L4 — No visual "locked" affordance** on the Journal "+" before tap; the state is only legible via the Settings row + the one-time reassurance. Deliberate (non-naggy) but consider a subtle persistent hint.
- **L5 — Restore doesn't distinguish offline** (all non-success → generic). Reuse the offline detection for a clearer message. — [SubscribeSheetViewModel.cs:232-258](Animal%20Diary%20App/Data/ViewModels/SubscribeSheetViewModel.cs#L232-L258).
- **L6 — Offer buttons aren't visually disabled during an in-flight purchase** (protected logically by `IsBusy`, but no dimming/spinner over the buttons).

---

## Best-practices checklist

**SDK configuration**
- ✅ Configure once, early, off the UI thread (`App.StartAsync` → `InitializeAsync`).
- ✅ Anonymous app-user-id (null id) — valid for a no-account app.
- ✅ Debug logs Debug-only (`AddRevenueCatBilling` defaults to `IsDebug()`; `Debug.WriteLine`/`[Conditional("DEBUG")]` stripped in Release).
- 🚫 Test Store key not separated from production (C1).
- ⚠️ Configure-once not concurrency-guarded (M7).

**Products / offerings**
- ✅ Prices come from the store (`PriceLocalized`), never hardcoded.
- ✅ Loading state + retry + offline vs generic messaging on the sheet.
- ⚠️ Offer list handed out as a live mutable collection (H2).
- ✅ Only maps `$rc_annual`/`$rc_monthly`; unknown packages ignored safely.

**Purchases**
- ✅ Double-tap guarded (`IsBusy`).
- ✅ Cancelled handled as non-error.
- ❌ Pending purchases handled (H4).
- 🚫 Already-owned handled (M1).
- ⚠️ Success-but-entitlement-inactive surfaced as failure (H4 — deliberate but collides with real delays).
- ✅ Access decided by entitlement, not by the transaction boolean.

**Entitlements**
- ✅ Checks `entitlement.IsActive`.
- ✅ Refresh on launch + resume + purchase + restore.
- ✅ Offline uses SDK cache (subscribers stay unlocked offline).
- ❌ Real-time update listener (H3 — binding limitation).
- ⚠️ No "unknown/loading" entitlement state → cold-launch lock race (H1).
- ⚠️ Exact-id match with silent "any active" fallback (M2).

**Restore**
- ✅ User-triggered restore button (not programmatic).
- ✅ Success/`NothingToRestore`/problem distinguished.
- ⚠️ Offline case not distinguished (L5).

**Gate / premium access**
- ✅ Single `HasFullAccess` gate; dose loop + reminders + export always free.
- ⚠️ Cold-launch race (H1).
- ❌ Complete coverage — Manage/Medications gates missing (H6).

**Lifecycle**
- ✅ Launch + resume refresh; trial starts at onboarding/first-pet.
- ⚠️ No mid-session expiry reflection until resume (H3).

**Error handling**
- ✅ Every SDK call wrapped; degrades to no-entitlement/no-offers, never crashes.
- ✅ Offline vs generic offers message.
- ❌ Pending (H4); ❌ already-owned (M1); ⚠️ no timeouts (M5).

**Security**
- ✅ Only the public SDK key ships (and it's git-ignored). No secret key in the app. RevenueCat is the server-side source of truth.
- ⚠️ Trial is client-side & clock-trustful (L1) — accepted.

**UX**
- ✅ Non-salesy paywall, subscribed/thank-you state, restore, manage.
- ⚠️ Manage URL Android-only (M3); ⚠️ no locked affordance (L4); ⚠️ buttons not dimmed mid-purchase (L6).

**Logging / analytics**
- ✅ Rich Debug diagnostics (`LogCustomerInfo`, app-user-id), Debug-only.
- ✅ `subscribe_screen_viewed` / `subscription_purchased` with source/plan/price.
- ❌ Production failure telemetry (M4); ⚠️ `days_active_in_trial` unimplemented (L3).

**Testing**
- ❌ Everything (H5) — no test project.

**Performance**
- ✅ Off-UI-thread init; cached entitlement; cheap cached offerings re-fetch.
- ⚠️ No bounded timeout on a stalled fetch (M5).

---

## Prioritized action plan

**Before any production build (release-blocking):**
1. **C1** — build-config-gate the API key (test in Debug, `goog_`/`appl_` in Release) + a startup guard that refuses a `test_` key in Release.
2. **C2** — restore `TrialLength = 14 days` (ideally `#if DEBUG` the short value).
3. **H1** — add a "entitlement known" state; don't lock during the pre-load window (optimistic-until-known).
4. **H2** — return an `Offers` snapshot (one-line fix, prevents a paywall crash).

**High-value next (correctness + trust):**
5. **H4 / M1** — model `Pending` and already-owned properly; stop reporting real successes/pending as failures.
6. **H6** — finish the read-only gates on Manage/Medications so "read-only" is honest.
7. **H5** — stand up a test project; cover the matrix below.
8. **M4** — add anonymous purchase/restore/offers failure telemetry.

**Then (robustness + polish):**
9. **H3** — document the listener limitation; add foreground refresh; evaluate a binding with the update listener.
10. **M3** (management URL), **M5** (timeouts), **M7** (init-once), **M2** (spaceless entitlement id + warn-on-fallback).

**Later / product decisions:**
11. **M6** caregiver billing policy, **L1** server-side trial hardening, **L2**–**L6** polish.

### Missing tests to add (H5)
Purchase success → entitlement active → gate lifts · purchase cancelled (no state change) ·
purchase pending · purchase failed (store error) · already-owned resolves to access ·
success-but-entitlement-inactive (config error) surfaced correctly · restore success /
nothing-to-restore / offline · trial active vs expired boundary (inject clock) ·
`HasFullAccess = trial OR entitlement` truth table · cold-launch unknown-entitlement not
locking (H1) · offline offers → offline message + retry recovers · plan mapping
($rc_annual/$rc_monthly/unknown) · `Null*` services always grant access · app resume
re-checks entitlement.
