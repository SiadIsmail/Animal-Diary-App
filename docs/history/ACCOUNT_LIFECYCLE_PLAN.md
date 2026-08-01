# Account Lifecycle — sign-out, account switching, and what happens to the pets

> Status: **built 2026-07-29** — Windows build clean, unit tests green. Slices A, B and C
> are done; §7 was resolved as "warn and let them choose". §8 remains deferred. The device
> tests in §6 are **not yet run** — that is the outstanding work.
>
> Written after a real data-loss incident during caregiver testing. Supersedes nothing; it
> fills a gap `CLOUD_SYNC_PLAN.md` left open — that plan specifies what a *sync* does, never
> what *signing out* does. Durable rules landed in [AI/domain.md](../../AI/domain.md) and
> [AI/design-decisions.md](../../AI/design-decisions.md); this file keeps the incident narrative.
>
> The sponsored-caregiver work (migration 0010, commit `c0b98ea`) is already shipped and is
> **not** the cause of anything here. Verified: `git diff 3951cb8 c0b98ea` touches zero lines
> of the purge condition, cursor handling, `KeyAccount`, `ResetSyncIdentityAsync` or
> `EnableBackupAsync`.

---

## 1. What happened

On one device: signed into account **X** with several pets → signed out → signed into **Y**
(one pet) → X's pets were removed locally → signed back into **X** → **nothing came back.**

The cloud data was never in danger. `PurgePetAsync` is a local hard delete that deliberately
does not tombstone and does not push, so X's rows were intact in Postgres the whole time.
But the device could no longer see them, and no in-app action would bring them back.

## 2. Root cause — three faults, one incident

**Fault 1 — sign-out only clears the session.** `CloudAuthService.SignOutAsync` clears the
session and nothing else. `cloud:backupEnabled` stays `1` and `cloud:lastAccount` stays `X`.

**Fault 2 — so signing into Y ran an ordinary sync, not `EnableBackupAsync`.** The membership
diff correctly found that X's pets aren't Y's and purged them locally. But
`EnableBackupAsync` is the **only** code that clears the pull cursors, and it never ran.

**Fault 3 — the cursors are a per-account high-water mark.** The pull is
`updated_at=gt.{cursor}` per table. After syncing as Y, every cursor sat at Y's latest
`updated_at`. Signing back into X, all of X's rows are *older* than that — so the pull
correctly returned nothing, permanently. The data was on the server and the device would
never ask for it again.

### The fault nobody hit yet

`EnableBackupAsync` calls `MarkAllDirtyAsync`, which queues **every local row** for upload.
Sign out of X, sign into Y, press "Enable backup" while X's pets are still on the device and
`handle_new_pet` makes **Y the owner of X's pets**. One person's medical records, silently
copied into another person's account.

This didn't fire only because the membership purge happened to run first. That is ordering
luck, not a guarantee, and it is the strongest reason to fix this properly rather than
patching the cursor.

## 3. The model

Two exits, and they must stay distinct — collapsing them into one "cloud off" switch is what
would make enabling backup a genuine one-way door:

| Action | Meaning | Local data |
|---|---|---|
| **Disable backup** | "this device stops syncing; these are still my pets" | **stays** |
| **Sign out** | "I am not this account on this device any more" | **goes** |

**Sign-out is the moment the data leaves, not the next sync.** The app already commits to
this rule for caregivers ([AI/domain.md](../../AI/domain.md)): *losing access purges the pet from
the device — medical data for a pet you no longer care for never stays behind.* Signing out
of X is losing access to X's pets. Today they linger until some later sync notices; hand the
phone to someone else in that window and the records are still sitting there.

**This makes absorb-on-enable unconditionally safe.** "Create an account and it adopts the
pets already on this device" is the correct onboarding story, but it is only correct while
the sole local data is data that belongs to nobody yet. Teardown at sign-out is what
guarantees that precondition.

**A pet is not lost, it is unbound from the device.** Signing back into X restores everything.
The sign-out copy has to say so (§5) — the current silence is what turns a reversible action
into a frightening one.

### Not a sign-out: an expired session

`CloudAuthService` also clears the session when a refresh token dies. That is the **same
account**, involuntarily — cursors remain valid and the user will sign back in. It must
**not** trigger teardown. Only a deliberate sign-out does.

---

## 4. Slice A — account teardown *(fixes the data loss)*

### A1. A teardown primitive that cannot be forgotten

Every account-scoped key the engine owns is already `cloud:`-prefixed
(`backupEnabled`, `lastAccount`, `lastSynced`, `firstBackupDone`, `cursor:*`, `memberships`,
`petAccess:v2`, `trialAnchor`). So the teardown is one prefixed delete, and a key added by a
future feature is covered automatically.

```csharp
// SyncStateStore
/// <summary>Delete every key under a prefix. The account-teardown primitive: all
/// account-scoped sync state is "cloud:"-prefixed, so one call cannot miss a key some
/// later feature added — the mistake that made a stale cursor outlive its account.</summary>
public Task ClearPrefixAsync(string prefix)
    => _db.Connection.ExecuteAsync("delete from \"SyncState\" where Key like ?", prefix + "%");
```

> **`TrialStartUtc` must NOT be cleared.** It lives in `AppSettings`, not `SyncState`, and is
> **device**-scoped on purpose. Clearing it would hand out a fresh 14-day trial on every
> sign-out. Only `cloud:trialAnchor` — the *reconciliation marker* — is account-scoped.

### A2. `ICloudSyncService.PrepareSignOutAsync()`

Ordering matters; each step exists because of a specific failure.

1. **Best-effort final push** if online — last chance for unsynced work.
2. **Count dirty rows.** If any remain, the caller warns (§5). See §7 for the open decision.
3. **Purge every pet belonging to the account being left** — everything with a `SyncId` in
   the membership map. Reuses `PurgePetAsync`, so reminders are cancelled and the active-pet
   selection is repaired exactly as for a revoked caregiver.
4. **`ClearPrefixAsync("cloud:")`.**

Auth must not depend on the sync engine, so callers orchestrate:
`await _sync.PrepareSignOutAsync(); await _auth.SignOutAsync();`
Call sites: `CloudSheetViewModel.SignOutAsync`, and the account-deletion path.

### A3. Defensive account-mismatch check at sync start

Belt and braces, and it repairs devices already in the broken state:

```csharp
// Cursors are a high-water mark for ONE account. If the signed-in account is not the one
// this state belongs to, every cursor is ahead of the new account's rows and the pull would
// silently return nothing — which is how switching away and back lost an entire library.
var lastAccount = await _state.GetAsync(KeyAccount);
if (lastAccount != null && lastAccount != session.UserId)
{
    foreach (var table in _tables)
        await _state.RemoveAsync(KeyCursorPrefix + table.CloudTable);
    await _state.RemoveAsync(KeyMemberships);
    await _state.RemoveAsync(KeyPetAccess);
    await _state.RemoveAsync(KeyTrialAnchor);
}
await _state.SetAsync(KeyAccount, session.UserId);
```

Deliberately **not** re-minting `SyncId`s here — that belongs to the intentional "move this
device's data to a new account" flow. Doing it on a plain sign-in would upload one account's
pets into another.

### A4. Fixes a live bug in the shipped caregiver work

`cloud:trialAnchor` is account-scoped but survives a sign-out today. After switching X→Y,
`ReconcileTrialAnchorAsync` compares the stored marker against the **device** anchor, finds
them equal, and skips — so **Y's `profiles.trial_started_at` is never claimed**, and Y would
not sponsor their caregivers during their trial. A3 clears it; A1 makes it structural.

---

## 5. Slice B — sign-out has to say what it does

Currently silent. That silence is what makes a reversible action feel like destruction.

- **Name the consequence and the reversal**, e.g. *"Bella, Max and 2 others will be removed
  from this device. They stay in your account and come back when you sign in."*
  Native `DisplayAlert` — the sanctioned surface for a destructive confirm (the bottom-sheet
  rule governs *input*, not confirmation).
- **Offer the vet export first**, exactly as pet deletion does. [AI/app-voice.md](../../AI/app-voice.md)
  §13 makes "always let people get their data out" a principle, and the export is free in
  every access state.
- **Warn about unsynced work** when step A2.2 finds dirty rows.
- EN + DE, German written native.

---

## 6. Slice C — docs, invariant, tests

**New invariant for [AI/domain.md](../../AI/domain.md):**

> All account-scoped sync state is `cloud:`-prefixed in `SyncState` and is torn down by a
> single `ClearPrefixAsync("cloud:")` on sign-out. A new account-scoped key must use that
> prefix — never `AppSettings`, which is device-scoped and survives sign-out. Mirrors the
> existing "every table in `InitAsync` must also be deleted in `AppResetService`" rule.

Also: a [AI/design-decisions.md](../../AI/design-decisions.md) entry for the two exits (disable
backup keeps data, sign-out does not) and why the alternative — keeping local copies after
sign-out — is worse: it re-opens cross-account absorption, leaves medical records with
someone who lost access, and contradicts the caregiver revocation rule.

**Tests.** The sync engine is MAUI/SQLite-bound so it isn't reachable from the current test
project; cover what is reachable and script the rest as device tests:

1. Sign out of X → X's pets gone locally → cloud row count for X unchanged.
2. Sign into Y → only Y's pets → **X's pets are not pushed into Y** (the §2 fault).
3. Sign back into X → **everything returns**.
4. Session-expiry does *not* purge.
5. Disable backup → local pets stay.
6. Existing device already in the broken state → one sync repairs it (A3).

---

## 7. Open decision — unsynced changes at sign-out, offline

The purge skips dirty rows (`if (pet.IsDirty) continue;`) because a never-synced pet exists
nowhere else and purging it destroys it outright. But a surviving dirty pet from X is then
absorbed into Y at the next enable — the copy problem again.

- **Block sign-out until it syncs** — safest for data, a signed-out-forever trap on bad signal.
- **Warn and let them choose** — *"3 changes haven't been backed up yet. Sign out anyway?"* ← **recommended**
- **Keep dirty rows silently** — simplest, re-opens cross-account copying.

Recommended: the warning. It matches how the app already handles destructive-but-legitimate
actions, and it puts the one genuinely unrecoverable case in front of the person who can
judge it.

---

## 8. Deferred, with reasons

- **Ownership transfer.** The real answer to "my pet is tied to this account" — one owner is
  structural (`pet_members_one_owner`), `remove_pet_member` refuses to remove the owner, and
  losing access to the owning email means losing the cloud copy. Already a Phase 3 candidate
  in [AI/current-roadmap.md](../../AI/current-roadmap.md); this plan makes the edge *visible* by
  removing the local copy that was papering over it, which strengthens the case.
- **Disable backup → sign in elsewhere → enable = copies the pets.** `ResetSyncIdentityAsync`
  re-mints ids and pushes them as new. For one person migrating between their own accounts
  that is a feature. For two people sharing a device it is the copy problem — but it requires
  deliberately disabling backup rather than signing out, which is not what anyone does before
  handing over a phone. Accepted; recorded so it is not rediscovered as a surprise.
- **`C1` from [BILLING_AUDIT.md](BILLING_AUDIT.md).** `BillingConfig.Enabled` is a plain
  `const` with no Debug/Release split. Lower risk now the key is a real `goog_` one, but
  nothing stops a future test key shipping. Unrelated to this plan.
