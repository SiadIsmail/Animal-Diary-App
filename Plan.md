Short Implementation Plan
Phase 1 — Database Structure

Create 3 tables:

Medication
- Id
- Name
- Dosage
- Notes
- IsActive
MedicationSchedule
MedicationSchedule
- Id
- MedicationId
- Type
- StartDate
- EndDate
- IntervalHours
- TimesJson
- DaysOfWeekJson
IntakeOccurrence
IntakeOccurrence
- Id
- MedicationId
- ScheduleId
- ScheduledTime
- TakenTime
- Status
Phase 2 — Schedule Types

Start with only:

FixedTimesDaily
EveryXHours
Weekly

Do NOT implement everything immediately.

Phase 3 — Schedule Creation UI

Simple flow:

User selects:
Daily
Every X hours
Specific weekdays

Then show:

TimePicker
Interval input
Weekday selector

Save schedule into SQLite.

Phase 4 — Occurrence Generator Service

Create:

ScheduleGeneratorService

Responsibility:

Generate next 14 days of occurrences

Example:

Vitamin D
08:00 daily

Creates:

May 12 08:00
May 13 08:00
May 14 08:00

Store in IntakeOccurrence.

Phase 5 — Daily View

Query:

WHERE ScheduledTime BETWEEN todayStart AND todayEnd

Show:

pending
taken
missed
Phase 6 — Intake Tracking

Buttons:

Taken
Skipped

Update:

TakenTime
Status

Never delete occurrences.

Phase 7 — Notifications

Create:

NotificationService

Responsibilities:

Read upcoming occurrences
Schedule local notifications

Run:

app startup
daily refresh
Phase 8 — Editing Schedules

When changing schedule:

DO:

deactivate old schedule
create new schedule
regenerate future occurrences

DO NOT:

rewrite history
Suggested Project Structure
/Models
    Medication.cs
    MedicationSchedule.cs
    IntakeOccurrence.cs

/Services
    MedicationService.cs
    ScheduleGeneratorService.cs
    NotificationService.cs

/Data
    AppDatabase.cs
MVP Goal


