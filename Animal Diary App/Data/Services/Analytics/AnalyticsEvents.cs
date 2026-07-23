namespace Animal_Diary_App.Data.Services.Analytics;

/// <summary>
/// The single source of truth for every analytics event name and property key.
/// Feature code references these constants instead of typing raw strings, so a
/// rename is one edit and typos can't fork an event into two.
///
/// Naming convention (kept in step with the existing website analytics so the app
/// and site can share one PostHog project):
/// <list type="bullet">
///   <item><b>snake_case</b> event names.</item>
///   <item><b>action-based</b>, human-readable (e.g. <c>pet_created</c>).</item>
///   <item>properties describe the <b>event</b>, never the user.</item>
/// </list>
///
/// PRIVACY RULE: no value placed under these keys may identify a person or reveal a
/// pet's care situation. Property values are coarse, non-free-text descriptors
/// (species bucket, entry type, day counts) — see the analytics docs.
/// </summary>
public static class AnalyticsEvents
{
    // ── App lifecycle ─────────────────────────────────────────────────────────
    /// <summary>App was launched (fired once startup has applied the language).</summary>
    public const string AppOpened = "app_opened";
    /// <summary>App was brought to the foreground by tapping a local notification.</summary>
    public const string NotificationOpened = "notification_opened";

    // ── Onboarding ────────────────────────────────────────────────────────────
    /// <summary>First-launch onboarding began (Welcome screen shown).</summary>
    public const string OnboardingStarted = "onboarding_started";
    /// <summary>The pet-creation form was opened (add/first-launch path, not edit).
    /// Sits between <see cref="OnboardingStarted"/> and <see cref="PetCreated"/> so
    /// form abandonment is visible.</summary>
    public const string PetFormStarted = "pet_form_started";
    /// <summary>On the onboarding condition picker's Continue, the pet had at least one
    /// condition configured.</summary>
    public const string ConditionSetupCompleted = "condition_setup_completed";
    /// <summary>On the onboarding condition picker's Continue, the pet had no condition
    /// (the user chose "None / Not sure" or skipped past). Never says <i>which</i>
    /// condition — only that setup was or wasn't done.</summary>
    public const string ConditionSetupSkipped = "condition_setup_skipped";
    /// <summary>First-launch onboarding finished (handed off into the tabbed app).</summary>
    public const string OnboardingCompleted = "onboarding_completed";

    // ── Account lifecycle ─────────────────────────────────────────────────────
    // These measure WHERE account creation drops off — they never identify the user.
    // No email, no user id, no code is ever attached (see the account_state note below).
    /// <summary>Sign-up was submitted and the account was created server-side (awaiting
    /// the emailed verification code).</summary>
    public const string SignUpStarted = "sign_up_started";
    /// <summary>The emailed sign-up code was verified — account creation completed. The
    /// gap from <see cref="SignUpStarted"/> is the email-verification drop-off.</summary>
    public const string SignUpVerified = "sign_up_verified";
    /// <summary>An email + password sign-in succeeded.</summary>
    public const string SignIn = "sign_in";
    /// <summary>A Google (browser/PKCE) sign-in succeeded. Measures completion of the
    /// browser flow, which can silently drop.</summary>
    public const string GoogleSignIn = "google_sign_in";

    // ── Sharing ───────────────────────────────────────────────────────────────
    /// <summary>An owner minted a caregiver invite code. The fact only — never the
    /// code, the pet, or any id.</summary>
    public const string PetShareInvited = "pet_share_invited";
    /// <summary>A caregiver redeemed an invite code and joined a shared pet. The fact
    /// only — never the code, the pet, or any id.</summary>
    public const string PetShareJoined = "pet_share_joined";

    // ── Pet management ────────────────────────────────────────────────────────
    /// <summary>A new pet was saved. Property: <see cref="PropSpecies"/>.</summary>
    public const string PetCreated = "pet_created";

    // ── Journal & tracking ────────────────────────────────────────────────────
    /// <summary>Something was logged in the Journal. Property: <see cref="PropEntryType"/>.
    /// One unified event covers all sheet types so "which logging features are used"
    /// is a single breakdown with no double-counting.</summary>
    public const string JournalEntryCreated = "journal_entry_created";
    /// <summary>A medication (with its reminder schedule) was created.
    /// Properties: <see cref="PropReminderCount"/>, <see cref="PropDaysPerWeek"/>.</summary>
    public const string MedicationCreated = "medication_created";

    // ── Engagement ────────────────────────────────────────────────────────────
    /// <summary>The Journal/calendar tab was opened.</summary>
    public const string CalendarOpened = "calendar_opened";
    /// <summary>The settings panel was opened.</summary>
    public const string SettingsOpened = "settings_opened";
    /// <summary>The Manage-pet page was opened. Feature-discovery signal: distinguishes
    /// "care management not wanted" from "not discovered".</summary>
    public const string ManagePetOpened = "manage_pet_opened";
    /// <summary>The vet-report export sheet was opened. Paired with
    /// <see cref="ReportExported"/> it gives an open→export conversion rate (was the
    /// report abandoned at the options screen?).</summary>
    public const string ExportSheetOpened = "export_sheet_opened";
    /// <summary>A vet-report PDF was successfully generated. Property:
    /// <see cref="PropRangeDays"/>.</summary>
    public const string ReportExported = "report_exported";

    /// <summary>The owner enabled cloud backup (account + opt-in). No properties —
    /// never the email or any account identifier.</summary>
    public const string CloudEnabled = "cloud_enabled";

    /// <summary>The first successful full sync after enabling completed.</summary>
    public const string CloudBackupCompleted = "cloud_backup_completed";

    // ── Property keys ─────────────────────────────────────────────────────────
    /// <summary>App display version, e.g. "1.3.1". Non-identifying.</summary>
    public const string PropAppVersion = "app_version";
    /// <summary>Active UI language, "en" / "de". Non-identifying.</summary>
    public const string PropLanguage = "language";
    /// <summary>OS platform bucket, "Android" / "iOS" / "WinUI" / "macOS".</summary>
    public const string PropPlatform = "platform";
    /// <summary>Coarse account state — <see cref="AccountStateAnonymous"/> /
    /// <see cref="AccountStateSignedIn"/> — attached to EVERY event by the central
    /// payload builder. It reports only <i>whether</i> an anonymous install is signed
    /// in to cloud, so signed-in vs anonymous behaviour, cloud adoption, and retention
    /// can be segmented. It is NOT an identity: no user id, email, or account link is
    /// ever derived from it, and the analytics <c>distinct_id</c> stays a random,
    /// rotatable GUID unrelated to the Supabase user.</summary>
    public const string PropAccountState = "account_state";
    /// <summary>Coarse species bucket (dog/cat/bird/rabbit/fish/other) — NEVER the
    /// free-text custom type a user might enter, which could be identifying.</summary>
    public const string PropSpecies = "species";
    /// <summary>Journal entry kind: mood/weight/glucose/appetite/seizure.</summary>
    public const string PropEntryType = "entry_type";
    /// <summary>How many reminder times per day a medication has (1–5). Not the name,
    /// dose, or schedule detail.</summary>
    public const string PropReminderCount = "reminder_count";
    /// <summary>How many weekdays a medication repeats on (1–7).</summary>
    public const string PropDaysPerWeek = "days_per_week";
    /// <summary>Report look-back window in days (30/90/180).</summary>
    public const string PropRangeDays = "range_days";

    // ── Property values (kept as constants so producers agree on spelling) ──────
    public const string EntryTypeMood = "mood";
    public const string EntryTypeWeight = "weight";
    public const string EntryTypeGlucose = "glucose";
    public const string EntryTypeAppetite = "appetite";
    public const string EntryTypeSeizure = "seizure";
    public const string EntryTypeWater = "water";
    public const string SpeciesOther = "other";

    /// <summary>Not signed in to cloud — the default state of every install.</summary>
    public const string AccountStateAnonymous = "anonymous";
    /// <summary>Signed in to a cloud account. Says nothing about <i>who</i>.</summary>
    public const string AccountStateSignedIn = "signed_in";
}
