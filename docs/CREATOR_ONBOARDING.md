# Onboarding a creator

> **For the owner.** The steps to set up one creator: their code, their Play link, and
> how to check it worked. Five minutes, once per creator.
>
> The creator-facing document is [CREATOR_PLAYBOOK.md](CREATOR_PLAYBOOK.md) (filming with
> the demo pets). This one is the part you do.

---

## What a creator gets

Two things, and they do different jobs:

| | What it is | Who uses it |
|---|---|---|
| **Code** | A word they say out loud: `THETO` | Their audience types it in the app |
| **Link** | A Play URL carrying the same code | Their audience just taps it |

The link is the one that actually works at scale — most people never type a code. The
code exists for anyone who hears about the app rather than tapping through, and for iOS
later, where links cannot carry attribution at all.

Neither one gives the user anything today. They are attribution only.

---

## 1. Pick the code

- **Their name, uppercase**, plus a suffix if it is a common word: `THETO`, `ZOLTRAK47`.
- Letters and digits only. It gets said aloud and typed by hand, so avoid `0`/`O` and
  `1`/`I` together.
- **Never reuse a code across creators.** It is the primary key; a second creator with the
  same code cannot be told apart from the first.

## 2. Add it

Supabase Dashboard → **SQL Editor**:

```sql
insert into public.creator_codes (code, creator, note)
values ('THETO', 'Theto', 'YouTube, deal signed 2026-08');
```

- `code` — uppercase. The app upper-cases what the user types, so they can type it any way.
- `creator` — the display name shown back to them ("We'll know you came from Theto") and
  the label on every analytics event. Spell it the way they spell it.
- `note` — for you. Where the deal came from, when.

This fails if the code already exists as an **access code** (migration 0019). That is
deliberate: access codes grant a year, and a duplicate would silently shadow it.

## 3. Build the link

```
https://play.google.com/store/apps/details?id=com.felova.app&referrer=creator%3DTHETO
```

Swap in their code. `%3D` is an encoded `=` and must stay encoded.

**Android only.** Apple's campaign tokens never reach the app, so on iOS the link still
installs the app but attributes nothing. Nothing to configure — just do not promise a
creator iOS numbers.

## 4. Send them

- The link, for descriptions and bio.
- The code, for saying out loud.
- If they are filming: [CREATOR_PLAYBOOK.md](CREATOR_PLAYBOOK.md) and the demo-pet code.

**Tell them the two codes are different things.** The demo code goes in the small `Code`
text at the very bottom of Settings; their creator code goes in the `Redeem a code` card
near the top. A creator holding both will otherwise try one in the other's box.

## 5. Check it worked

```sql
select * from public.creator_code_stats order by purchases desc;
```

| column | means |
|---|---|
| `accounts_entered` | distinct accounts that arrived by **any** route |
| `from_link` | of those, how many carried the install link |
| `typed_in` | of those, how many typed the code |
| `purchases` | first purchases credited to them |

`from_link` and `typed_in` **overlap** — one person can do both — so they can sum to more
than `accounts_entered`. They are two views of the same people, not a split.

In PostHog, break any insight down by `referral_source` to compare their audience against
`none`.

---

## Worth knowing

- **The code must be entered before the purchase.** Attribution is stamped at purchase
  time and is immutable afterwards.
- **Last touch wins.** If someone arrives through one creator's link and later types
  another's code, the purchase is credited to the code they typed.
- **Only first purchases are credited**, never renewals.
- **Coverage is never 100%.** Report "attributed purchases", not "your purchases".
- **Testing the link needs a fresh install.** Uninstall, tap the link, install. An
  already-installed phone will never pick up a referrer, which looks exactly like a bug.
