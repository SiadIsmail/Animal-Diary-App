# Monetization — Free Trial + RevenueCat Subscription

> Status: **proposal / plan** — nothing in this document is built yet.
> Scope: a time-limited free trial that, on expiry, puts the app into a
> **care-only read state** until the user subscribes through RevenueCat. Owner
> decisions recorded 2026-07-25.
>
> **This supersedes [CLOUD_SYNC_PLAN.md](CLOUD_SYNC_PLAN.md) §9**, which recorded
> "there is no premium tier today… zero billing code, zero trial timers, zero
> entitlement checks." That decision is now reversed. The `profiles.plan` column
> that §9 kept open for exactly this remains the future cloud-side hook, but the
> gate described here is device-local and does not depend on it.

---

## 1. Philosophy (read first — it governs every other section)

Felova is a safety net for a sick animal's medication routine. The README and
[AI/app-voice.md](AI/app-voice.md) build the whole product around one promise:
the owner can go to bed knowing the 8pm dose happened, and walk into the vet with
an answer instead of a guess. **Monetization is not allowed to break that
promise.** A paywall that stops someone recording an insulin dose, or silences a
seizure-med reminder on day 25, is both off-brand and a real-world safety risk.

So the trial gate is built on one rule:

> **You can always keep caring for the pet you already set up. What you pay for
> is adding more.**

Three things are therefore **never** gated, on any plan, at any time:

1. **Medication reminders keep firing.** The read-only state must not cancel a
   single scheduled reminder. Cancelling already-scheduled medical reminders
   behind a paywall is indefensible for this audience. (Engineering note: this
   is a P0 invariant with a test, in the same spirit as the paused-pet rule in
   app-voice §15.)
2. **The scheduled-dose adherence loop stays free.** Marking an already-scheduled
   dose as *given* or *skipped* is the core safety action and is never blocked.
3. **The vet PDF export stays free.** app-voice §13 makes "always let people get
   their data out" a core principle. A user's own pet's medical history is never
   trapped behind the wall. They can always take what they already logged.

What the trial gate *does* restrict, after expiry, is **new creation and new
logging** — the growth of the record, not the maintenance of it.

**Voice rule for the paywall.** The paywall is the most marketing-adjacent
surface in a product whose voice doc bans marketing. It stays plain and honest:
what it costs, what keeps working, no urgency, no guilt, no red, no implication
the pet is at risk. See §6.

---

## 2. What RevenueCat does and does not do here

RevenueCat manages **store subscriptions and entitlements** — "is this user's
purchase currently active?" — by wrapping StoreKit (iOS) and Play Billing
(Android). It does **not** provide the "24 free days" timer. That trial window is
an **app-level concept Felova owns**.

Two independent concepts, combined into one gate:

| Concept | Owner | Source of truth |
|---|---|---|
| **Trial window** | Felova | local `TrialStartUtc` + a fixed length (24 days) |
| **Entitlement** | RevenueCat | `CustomerInfo.Entitlements["premium"].IsActive` |

```
HasFullAccess  =  entitlementActive  ||  withinTrial
withinTrial    =  (UtcNow - TrialStartUtc) < TrialLength   // 24 days
```

**Why a native SDK is unavoidable here** (and why the house "no SDK, hand-built
HTTP" rule from the cloud/analytics layers does not transfer): in-app purchases
must legally go through the native store billing APIs. They cannot be done over
REST. RevenueCat's entire value is wrapping those native APIs. So a native MAUI
binding is required. The *which binding* question is the main technical unknown
and gets a spike before anything else is built (§8, slice 1), exactly as
`supabase-csharp` was spiked on Android before the cloud engine.

---

## 3. Trial length and anchor

- **Length:** 24 days (a single constant; easy to tune during testing).
- **Anchor:** `TrialStartUtc`, stored in the existing `AppSettings` key-value
  table via `SettingsService`, set once on the first launch that reaches the main
  app (i.e. after the first pet exists).
- **Accepted limitation (owner, 2026-07-25):** a reinstall / clear-data resets
  the anchor and hands out a fresh 24 days. This is deliberately **not** hardened.
  A reinstall wipes all of the user's local pet data too, so restarting the trial
  costs a serious owner everything they logged — the abuse path is self-defeating.
  Server-side anchoring (Supabase `profiles.created_at`, or RevenueCat first-seen)
  stays available as a future hardening if data ever shows it is needed.

---

## 4. What locks and what stays free

The gate is `IEntitlementService.HasFullAccess`. When it is `false` (trial
expired and not subscribed), the app is in **care-only** state.

**Always free (never gated, any state):**

- Viewing everything — calendar, timeline, pet profiles, full history.
- **Marking an already-scheduled dose given / skipped** (the adherence loop).
- **Medication reminders firing** (never cancelled).
- **Vet PDF export** of already-logged data.
- Settings, language switch, cloud sign-in, and sync/backup of existing data.
- Subscribe / Restore purchases.

**Blocked when locked → routes to the paywall sheet:**

- Adding a new pet.
- Adding, editing, or archiving a medication or its schedule.
- Adding or editing any journal / symptom entry: mood, weight, glucose,
  appetite, seizure, water, notes.
- Adding or editing a condition, care plan, or tracker.
- Editing an existing pet's profile.
- Inviting a caregiver / creating an invite code (a structural change — *confirm
  with owner*; sharing is arguably part of "adding more").

**The clean line:** the scheduled-dose logging path (`MedicationDoseLog`, the
"still to do" chip row, one-tap dose logging) stays enabled; the add/create/edit
paths (`PetEntryService`, `MedicationService`, the per-type input sheets, care
plan/tracker/condition writes) are the ones gated. These are already distinct
commands at the ViewModel layer, so distinguishing them is a per-entry-point
check, not a per-field one.

---

## 5. Architecture — mirror the cloud boundary

New folder `Data/Services/Billing/`, following the
`ICloudSyncService` / `NullCloudSyncService` pattern the README calls out. No
RevenueCat type leaks past this folder (same rule the cloud boundary follows for
Supabase types).

```
Data/Services/Billing/
  IEntitlementService.cs        // the boundary
  RevenueCatEntitlementService.cs   // wraps the RC binding (Android/iOS)
  NullEntitlementService.cs     // Windows/macOS dev: HasFullAccess = true always
  BillingConfig.cs              // RC public API keys per platform (safe to embed)
```

**`IEntitlementService` surface:**

```csharp
public interface IEntitlementService
{
    bool       HasFullAccess    { get; }   // entitlementActive || withinTrial
    AccessState State           { get; }   // Trial | TrialExpired | Subscribed
    int        TrialDaysLeft    { get; }
    event Action? StateChanged;

    Task InitializeAsync();                 // read trial anchor, fetch CustomerInfo
    Task RefreshAsync();                    // re-check entitlement (resume/foreground)
    Task<bool> PurchaseAsync(object package);
    Task RestoreAsync();
}
```

- **`NullEntitlementService`** is registered on Windows/macOS (no store), returns
  `HasFullAccess = true` always, so dev builds never lock. Directly mirrors
  `NullCloudSyncService`.
- Registered per-platform in `MauiProgram`, like the cloud services.
- `InitializeAsync` / `RefreshAsync` run off the UI path from `App.StartAsync`
  and `OnResume`, alongside the existing reminder catch-up and cloud sync —
  quiet, defensive, never blocking startup (same wrapping those already use).

**The gate binding.** Writes are not funnelled through one chokepoint (each
service writes its own table), so read-only is enforced at the command/VM layer.
Expose `bool CanEdit => _entitlements.HasFullAccess` on `BaseViewModel` (or a
small `IAccessGate`), bound to `IsEnabled`/`IsVisible` on every add/edit control.
Each add-entry command and each `FelovaBottomSheet`-opening command checks the
gate; when locked it opens the **paywall sheet** instead of the input sheet. One
check per entry point.

---

## 6. Paywall UI — a `FelovaBottomSheet`, not RevenueCat's native paywall

RevenueCat's drag-and-drop paywall templates are native views that MAUI bindings
expose poorly and that would clash with the design system. The README rule is
absolute: **every in-app surface is the shared `FelovaBottomSheet`** — no modals,
no third-party popups.

- Build **`PaywallSheetView` + `PaywallSheetViewModel`** on `FelovaBottomSheet`,
  styled like `CloudSheetView` / `KeepSafePage`.
- Prices and product titles come from RevenueCat `Offerings` (store-localized) —
  **never hardcode a price** into a string.
- **Restore purchases** is present (required for store approval).

**Two placements (both requested):**

1. **Gently, once, after onboarding.** `KeepSafePage.HandOff()` is the seam — it
   already runs the "gentle and skippable" post-onboarding offer and calls
   `SwitchToMainApp()`. During the trial the paywall shows here as a soft,
   fully skippable sheet ("Not now"), informational rather than a wall. app-voice
   §17 wants onboarding cut to the bone and bans warm asides there, so this sheet
   stays plain and dismissible — never a wall while the trial is still running.
2. **A settings row.** A "Felova subscription" row (final wording per §6.1 word
   bans) in `SettingsPanelView` / `SettingsViewModel`, opening the same sheet —
   mirrors the existing `OpenCloudCommand` wiring.

**On expiry**, the paywall is where every gated edit attempt routes. The user can
still read, export, and run the dose loop; only *adding more* leads here.

**Copy constraints (app-voice §23 checklist applies):** no startup vocabulary
(§6.1: no "unlock", "peace of mind", "take control", "all-in-one"), zero
exclamation points / emoji / em dashes (§5), no fabricated user counts or
testimonials (§16), **no handcrafted lines** (§4.3 bans them on transactional
paths). The lock state never guilts, never uses red, never implies the pet is at
risk. All strings in **both** `AppStrings.resx` and `AppStrings.de.resx`; German
written native, not translated (§19). Paywall copy gets owner sign-off — it is
the single hardest screen on which to hold the voice.

---

## 7. Analytics

Coarse, anonymous events matching the existing `AnalyticsEvents` style (no PII,
no prices, no ids — README rule): `paywall_shown`, `trial_expired`,
`purchase_started`, `purchase_completed`, `purchase_restored`.

---

## 8. Phased slices (owner test-pause between each, per the cloud-plan rhythm)

### Slice 1 — Spike the binding *(the real risk)*
- Evaluate a MAUI RevenueCat binding (community `Plugin.Maui.RevenueCat`, or a
  thin binding over `purchases-hybrid-common`) on Android with a **real sandbox
  purchase and restore**. Decide the dependency. Nothing else starts until this
  is known-good, exactly as `supabase-csharp` was proven on Android first.

### Slice 2 — Boundary + trial logic, no UI
- `IEntitlementService`, `RevenueCat…` + `Null…` impls, `BillingConfig`.
- `TrialStartUtc` in `SettingsService`; `HasFullAccess` / `State` / `TrialDaysLeft`.
- Init/refresh wired off the UI path in `App`. Windows/macOS dev unaffected.
- Ship and soak. No visible change while the trial is active.

### Slice 3 — Paywall sheet + the gate
- `PaywallSheetView` / VM on `FelovaBottomSheet`.
- `CanEdit` bindings on every add/create/edit control; gated commands route to
  the paywall. Care-only state enforced per §4 (dose loop, reminders, export
  stay free).
- Post-onboarding placement in `KeepSafePage`; settings row.

### Slice 4 — Store config + copy + docs
- RevenueCat dashboard: entitlement `premium`, offerings, products.
- App Store Connect + Play Console subscription products.
- EN/DE strings, restore flow, the §7 analytics events.
- Update [CLOUD_SYNC_PLAN.md](CLOUD_SYNC_PLAN.md) §9 to point here.

---

## Resolved decisions (owner, 2026-07-25)

1. **Trial length:** 24 days (tunable constant), then care-only read state.
2. **Read-only scope:** the scheduled-dose adherence loop stays free; medication
   reminders keep firing; vet export stays free. New pets, new/edited
   medications, and all new/edited journal & symptom entries are gated. "You can
   keep caring for the pet you set up; you pay to add more."
3. **Trial anchor:** local `TrialStartUtc`. Reinstall-reset accepted and not
   hardened — a reinstall wipes the owner's data too, so the abuse path is
   self-defeating for any serious owner.
4. **Export while locked:** always free. Users can take out what they already
   logged; they just cannot log or create anything new.
5. **Provider:** RevenueCat, wrapped behind an `IEntitlementService` boundary in
   `Data/Services/Billing/`, mirroring the cloud boundary. Native binding
   required (purchases can't go over REST).

## Open questions

- **Sharing/invites while locked** — gated as "adding more", or kept free? (§4)
- **Price point and subscription cadence** (monthly / annual / both) — set on the
  RevenueCat dashboard, surfaced from `Offerings`, never hardcoded.
- **Binding choice** — resolved by slice 1's spike.
