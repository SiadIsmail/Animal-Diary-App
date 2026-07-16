# Current Roadmap

> **For AI assistants: this is what is intentionally *not* built yet.** It exists
> so you don't mistake an unbuilt feature for a bug, and so new work follows the
> path already chosen. Keep it short: move an item out the moment it ships (record
> the durable knowledge in the other AI docs, not here). This is not a backlog of
> ideas — only near-term, decided direction.

---

## Near-term (planned, path already known)

- **Water Journal sheet.** Water is a valid tracker but has no input sheet yet —
  it is filtered out of the chip row. Add it via the sheet contract in
  [coding-standards.md](coding-standards.md) when logging is needed.
- **Medication detail view.** Manage-page medication rows currently open the
  medications list; a dedicated detail page is pending.
- **Adherence streak stats.** The dose-log data exists; the summary/streak UI on
  the calendar is not built.
- **Vet report polish.** A share sheet, an on-device preview, and a user-facing
  range picker (the range is already a `GenerateAsync` parameter).

## Later

- **Higher-reliability Android trigger tier** (custom `setAlarmClock`
  AlarmManager + dispatcher) plus a user-facing "reminder health" /
  battery-optimization guide.
- **Additional reminder types** on the same instance-based architecture — mood
  check-ins, weigh-in reminders, vet appointments (enum / id-helper / message
  stubs already exist).
- **Per-notification deep links** — tapping a reminder opens the relevant
  pet/med.
