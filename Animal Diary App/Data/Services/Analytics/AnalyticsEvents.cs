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
    /// <summary>A user-visible session began: the app was opened, or returned to after
    /// being away longer than <see cref="AnalyticsSession.IdleTimeout"/>.
    ///
    /// <para>NOT "the process started". It fires from window creation and resume, never
    /// from <c>App.StartAsync</c>, because a reboot or a Play Store update starts the
    /// process headlessly with no window and no user — those were being counted as
    /// launches. And it is session-gated rather than once-per-process, because a process
    /// that survives a week of daily use would otherwise report a single open. Both
    /// corrections matter for the "came back later" funnel step, which reads this event
    /// filtered on <see cref="PropDaysSinceInstall"/>.</para></summary>
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
    /// <summary>The owner recorded an outcome for a scheduled dose — the app's core daily
    /// action. Property: <see cref="PropDoseStatus"/>.
    ///
    /// <para>Fires on all three user gestures (one-tap chip, "Mark as given", "Mark as
    /// skipped") and <b>only</b> on those: the reconciler's automatic Missed stamping is
    /// machine-generated, not a person tending to their pet, which is why this is tracked
    /// at the gesture layer and not in <c>MedicationDoseLogService</c>. Never the
    /// medication, dose, time, or pet.</para>
    ///
    /// <para>Without this, the most frequent thing anyone does in Felova was invisible
    /// after the first one (folded into <see cref="FirstLogCompleted"/>), so retention
    /// could only be measured on journal sheets and understated the most engaged
    /// users.</para></summary>
    public const string DoseLogged = "dose_logged";
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

    /// <summary>The Constellation (the pet's history as a night sky) was opened.
    /// Property: <see cref="PropRangeDays"/> — which stretch of time was being looked
    /// at, never how much is in it. Feature-discovery signal for a surface reachable
    /// only from Quick management.</summary>
    public const string ConstellationOpened = "constellation_opened";

    /// <summary>A picture of the Constellation reached the OS share sheet. Property:
    /// <see cref="PropRangeDays"/>. Never the pet, the name, or anything in the
    /// picture — this counts that a share happened, nothing about what was shared.</summary>
    public const string ConstellationShared = "constellation_shared";

    /// <summary>The owner enabled cloud backup (account + opt-in). No properties —
    /// never the email or any account identifier.</summary>
    public const string CloudEnabled = "cloud_enabled";

    /// <summary>The first successful full sync after enabling completed.</summary>
    public const string CloudBackupCompleted = "cloud_backup_completed";

    // ── Trial & subscription funnel ───────────────────────────────────────────
    // Coarse and anonymous like everything else: no price is a person, no plan is an
    // identity. With ~10 users the shape of the funnel + the founder's conversations
    // are the signal, not the counts.
    /// <summary>The app-side free trial clock started (once, at onboarding completion).</summary>
    public const string TrialStarted = "trial_started";
    /// <summary>The user completed their first real log (dose given or journal entry).
    /// The bonding milestone the trial explainer waits for.</summary>
    public const string FirstLogCompleted = "first_log_completed";
    /// <summary>The reassuring post-first-log trial explainer was shown.</summary>
    public const string TrialExplainerShown = "trial_explainer_shown";
    /// <summary>The single pre-trial-end nudge was shown. Property: <see cref="PropTrialDay"/>.</summary>
    public const string PreEndNudgeShown = "pre_end_nudge_shown";
    /// <summary>The app entered the care-only read state (trial elapsed / subscription
    /// lapsed) and showed the reassurance. Property: <see cref="PropTrialDay"/>.</summary>
    public const string ReadOnlyEntered = "read_only_entered";
    /// <summary>A caregiver's cover under someone else's subscription ended (that owner
    /// lapsed or their trial ran out) and we said so. The fact only — never which pet,
    /// which owner, or how many people were affected.</summary>
    public const string SponsorshipEnded = "sponsorship_ended";
    /// <summary>The subscribe sheet was viewed. Property: <see cref="PropSubscribeSource"/>
    /// — this is the "where were they when they considered paying" signal.</summary>
    public const string SubscribeScreenViewed = "subscribe_screen_viewed";
    /// <summary>A subscription was purchased. Properties: <see cref="PropPlan"/>,
    /// <see cref="PropPrice"/>, <see cref="PropSubscribeSource"/>.</summary>
    public const string SubscriptionPurchased = "subscription_purchased";
    /// <summary>A purchase attempt did not complete. Property: <see cref="PropReason"/>
    /// (coarse bucket — never a store message). This is how purchase friction is visible
    /// in production, where debug logs aren't.</summary>
    public const string PurchaseFailed = "purchase_failed";
    /// <summary>A restore attempt failed (not "nothing to restore"). Property:
    /// <see cref="PropReason"/>.</summary>
    public const string RestoreFailed = "restore_failed";
    /// <summary>The subscribe sheet finished loading with no offers to show. Property:
    /// <see cref="PropReason"/> (offline vs empty) — surfaces store/config/connectivity
    /// friction that would otherwise be invisible.</summary>
    public const string OffersLoadFailed = "offers_load_failed";
    /// <summary>An access code was typed in and submitted. Property:
    /// <see cref="PropOutcome"/> — redeemed / invalid / already_used / rate_limited /
    /// offline / failed.
    ///
    /// <para><b>Never carries the code, the campaign, the grant length, or the resulting
    /// expiry.</b> Campaign attribution ("how many came from Reddit") is answered in
    /// Postgres by <c>access_code_stats</c>, where it is exact; these events are anonymous
    /// and cannot be joined to an account, so a campaign property here would be a worse
    /// number bought with a weaker posture. This event measures friction only: are people
    /// mistyping codes, hitting the attempt cap, or bouncing off the account requirement.</para></summary>
    public const string AccessCodeRedeemed = "access_code_redeemed";
    /// <summary>A creator's code was entered (migration 0016). <b>No properties, ever</b> —
    /// not even the creator. Which creator someone came through is answered by
    /// <c>creator_code_stats</c> in Postgres, where it is exact and joined to real purchases;
    /// putting it on an anonymous event would be a worse number bought with a weaker posture,
    /// and at influencer-campaign volumes a creator tag edges toward identifying. This event
    /// exists only to show that the box is being used at all.</summary>
    public const string CreatorCodeEntered = "creator_code_entered";
    /// <summary>The one heads-up before a redeemed access code's grant ends. The grant
    /// sibling of <see cref="PreEndNudgeShown"/>; no properties (its "day" would be the
    /// grant length, which identifies the campaign).</summary>
    public const string GrantEndingShown = "grant_ending_shown";
    /// <summary>The care-only read state began because a GRANT ran out rather than a trial.
    /// Separate from <see cref="ReadOnlyEntered"/> so the two are not silently pooled: they
    /// describe different people reaching the same screen.</summary>
    public const string GrantEnded = "grant_ended";

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
    /// <summary>Which creator this install came through, or <see cref="ReferralSourceNone"/>
    /// — attached to EVERY event by the central payload builder, exactly like
    /// <see cref="PropAccountState"/>, so any existing funnel can be split by channel
    /// without a second event stream or a single new event.
    ///
    /// <para>It carries the creator's <b>display name</b>, never the code. A channel label
    /// is the same character of fact as the platform or the language: it says how the app
    /// was found, not who found it. The code is closer to a token and stays out of
    /// analytics entirely, as does the account — events keep
    /// <c>$process_person_profile = false</c>, so nothing here can be joined into a person
    /// profile.</para>
    ///
    /// <para>Exact per-creator <i>revenue</i> attribution is still answered in Postgres by
    /// <c>creator_code_stats</c>, which joins to real purchases. This property answers the
    /// different question those tables cannot: how people who arrived through a creator
    /// <i>behave</i> — do they onboard, log, come back.</para></summary>
    public const string PropReferralSource = "referral_source";
    /// <summary>Coarse age of the install in UTC calendar days, as a bucket
    /// (<c>0</c>/<c>1</c>/<c>2-3</c>/<c>4-7</c>/<c>8-14</c>/<c>15+</c> — see
    /// <see cref="AnalyticsTenure"/>). Attached to EVERY event by the central payload
    /// builder. It exists because a PostHog funnel has only a <i>maximum</i> conversion
    /// window and cannot require that a step happen a day or more after the previous one;
    /// as a property, "came back on a later day" becomes a plain filter
    /// (<c>app_opened where days_since_install != 0</c>). Bucketed, not exact: a precise
    /// install age alongside a timestamp is a far narrower fingerprint, and the coarse
    /// value answers every question we actually ask of it.</summary>
    public const string PropDaysSinceInstall = "days_since_install";
    /// <summary>Coarse species bucket (dog/cat/bird/rabbit/fish/other) — NEVER the
    /// free-text custom type a user might enter, which could be identifying.</summary>
    public const string PropSpecies = "species";
    /// <summary>Journal entry kind: mood/weight/glucose/appetite/seizure/water.</summary>
    public const string PropEntryType = "entry_type";
    /// <summary>What the owner recorded for a dose — <see cref="DoseStatusTaken"/> /
    /// <see cref="DoseStatusSkipped"/>. Deliberately the only property on
    /// <see cref="DoseLogged"/>: whether it was logged from the chip or after the fact
    /// from the timeline would be interesting, but it is not needed to answer "did they
    /// keep caring for the pet", and data minimization wins ties.</summary>
    public const string PropDoseStatus = "dose_status";
    /// <summary>How many reminder times per day a medication has (1–5). Not the name,
    /// dose, or schedule detail.</summary>
    public const string PropReminderCount = "reminder_count";
    /// <summary>How many weekdays a medication repeats on (1–7).</summary>
    public const string PropDaysPerWeek = "days_per_week";
    /// <summary>Report look-back window in days (30/90/180).</summary>
    public const string PropRangeDays = "range_days";
    /// <summary>Where the subscribe sheet was opened from — <see cref="SubscribeSourceSettings"/>
    /// / <see cref="SubscribeSourceNudge"/> / <see cref="SubscribeSourceReadOnly"/> /
    /// <see cref="SubscribeSourceExplainer"/>.</summary>
    public const string PropSubscribeSource = "source";
    /// <summary>Subscription cadence — <see cref="PlanYearly"/> / <see cref="PlanMonthly"/>.</summary>
    public const string PropPlan = "plan";
    /// <summary>Store-formatted price string (e.g. "€24.99"). Not personal.</summary>
    public const string PropPrice = "price";
    /// <summary>Which day of the trial an event happened on (1-based). Coarse
    /// engagement/timing signal; not identifying.</summary>
    public const string PropTrialDay = "trial_day";
    /// <summary>Coarse failure bucket for billing events — <see cref="ReasonFailed"/> /
    /// <see cref="ReasonUnavailable"/> / <see cref="ReasonOffline"/> / <see cref="ReasonEmpty"/>.
    /// Never a raw store message.</summary>
    public const string PropReason = "reason";
    /// <summary>How an attempt ended, success included — distinct from
    /// <see cref="PropReason"/>, which only ever describes a failure. Used by
    /// <see cref="AccessCodeRedeemed"/>. Coarse buckets only; never server text.</summary>
    public const string PropOutcome = "outcome";

    // ── Property values (kept as constants so producers agree on spelling) ──────
    public const string EntryTypeMood = "mood";
    public const string EntryTypeWeight = "weight";
    public const string EntryTypeGlucose = "glucose";
    public const string EntryTypeAppetite = "appetite";
    public const string EntryTypeSeizure = "seizure";
    public const string EntryTypeWater = "water";

    /// <summary>An entry against a tracker the owner defined. Deliberately ONE
    /// bucket for all of them: the tracker's name is free text the owner typed, and
    /// sending it would say what their animal is being treated for. Same rule that
    /// buckets a custom pet type to "other" (see AI/analytics.md).</summary>
    public const string EntryTypeCustom = "custom";
    public const string SpeciesOther = "other";

    // Dose outcomes the owner can record. A skip is a first-class fact here exactly as it
    // is in the vet report — non-adherence is still the owner tending to the pet, and both
    // values count as engagement for retention.
    public const string DoseStatusTaken = "taken";
    public const string DoseStatusSkipped = "skipped";

    // Subscribe-sheet sources.
    public const string SubscribeSourceSettings = "settings";
    public const string SubscribeSourceNudge = "nudge";
    public const string SubscribeSourceReadOnly = "read_only";
    public const string SubscribeSourceExplainer = "explainer";
    public const string SubscribeSourceSponsorshipEnded = "sponsorship_ended";
    // Subscription plans.
    public const string PlanYearly = "yearly";
    public const string PlanMonthly = "monthly";
    // Billing failure reasons (coarse buckets).
    public const string ReasonFailed = "failed";
    public const string ReasonUnavailable = "unavailable";
    public const string ReasonOffline = "offline";
    /// <summary>The store account owns a subscription held by a DIFFERENT app account, so it
    /// cannot be granted here. Worth its own bucket: it is not a failure to fix, it is a
    /// support conversation, and its rate tells you how often people switch accounts.</summary>
    public const string ReasonOwnedByOtherAccount = "owned_by_other_account";
    public const string ReasonEmpty = "empty";

    /// <summary>Not signed in to cloud — the default state of every install.</summary>
    public const string AccountStateAnonymous = "anonymous";
    /// <summary>Signed in to a cloud account. Says nothing about <i>who</i>.</summary>
    public const string AccountStateSignedIn = "signed_in";

    /// <summary>No creator attached to this install — organic, or a link/code that was never
    /// used. An explicit bucket rather than a missing property, so "organic" is filterable
    /// and comparable instead of being an absence.</summary>
    public const string ReferralSourceNone = "none";
}
