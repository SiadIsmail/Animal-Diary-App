namespace Animal_Diary_App.Data.Services.Reports.Document;

using Animal_Diary_App.Helpers;

/// <summary>
/// Every word the vet report prints, pulled from the localized resources — the same
/// arrangement <see cref="Animal_Diary_App.Data.Services.Notifications.NotificationMessages"/>
/// uses for notification copy, and for the same reason: a section should compose a
/// layout, not carry copy.
///
/// The report used to hardcode its structural labels in English while dates and
/// decimals followed the device culture, so a German owner handed their vet a page
/// reading "MEDICATIONS" over "03 Juli 2026". Every label now comes from here.
///
/// Two rules this file exists to keep:
/// <list type="bullet">
/// <item><b>Resolve per call, never cache.</b> Every member is a property or method
///   that asks <see cref="LocalizationManager"/> at render time. A
///   <c>static readonly</c> array of row labels would freeze the language the app
///   happened to start in, and the app switches language live.</item>
/// <item><b>Structural labels only.</b> Stored data — pet names, medication names,
///   foods, the owner's notes — is printed exactly as it was entered and is never
///   translated. The scale labels below are the app's own vocabulary for a level, not
///   owner text, which is why water and appetite reuse the journal's existing keys
///   rather than introducing a second translation of the same five words. Mood is the
///   documented exception; see <see cref="MoodRows"/>.</item>
/// </list>
/// </summary>
public static class VetReportStrings
{
    private static LocalizationManager L => LocalizationManager.Instance;

    // ── Document chrome ───────────────────────────────────────────────────────
    public static string Footer => L.GetString("Report_Footer");
    public static string RunningTitle(string petName) => L.Format("Report_RunningTitle", petName);
    public static string Generated(string date) => L.Format("Report_Generated", date);
    public static string Page => L.GetString("Report_Page");
    public static string PageOf => L.GetString("Report_PageOf");

    // ── Header ────────────────────────────────────────────────────────────────
    public static string Conditions => L.GetString("Report_Conditions");
    public static string Weight => L.GetString("Report_Weight");
    public static string Owner => L.GetString("Report_Owner");
    public static string AgeYears(int years) => L.Format("Report_AgeYears", years);
    public static string WeightChange(string signedKg) => L.Format("Report_WeightChange", signedKg);

    // ── Medications ───────────────────────────────────────────────────────────
    public static string SectionMedications => L.GetString("Report_SectionMedications");
    public static string ColMedication => L.GetString("Report_ColMedication");
    public static string ColDose => L.GetString("Report_ColDose");
    public static string ColFrequency => L.GetString("Report_ColFrequency");
    public static string ColAdherence => L.GetString("Report_ColAdherence");
    public static string FrequencyPerDay(int timesPerDay, string times) => L.Format("Report_FreqPerDay", timesPerDay, times);
    public static string FrequencyDaysPerWeek(int days, string times) => L.Format("Report_FreqDaysPerWeek", days, times);
    public static string AdherenceGiven(int taken, int scheduled) => L.Format("Report_AdherenceGiven", taken, scheduled);
    public static string AdherenceUnscheduled(int taken) => L.Format("Report_AdherenceUnscheduled", taken);
    public static string AdherenceSkipped(int count) => L.Format("Report_AdherenceSkipped", count);
    public static string AdherenceMissed(int count) => L.Format("Report_AdherenceMissed", count);

    // ── Trends ────────────────────────────────────────────────────────────────
    public static string SectionTrends => L.GetString("Report_SectionTrends");
    public static string SeriesWeight => L.GetString("Report_SeriesWeight");
    public static string SeriesGlucose => L.GetString("Report_SeriesGlucose");
    public static string SeriesSeizuresPerWeek => L.GetString("Report_SeriesSeizuresPerWeek");

    /// <summary>Trailing half of the one-reading line: "5.2 kg <b>on 03 Jul 2026</b>".</summary>
    public static string OnDate(string date) => L.Format("Report_OnDate", date);

    // ── Mood / water / appetite ───────────────────────────────────────────────
    public static string SectionMood => L.GetString("Report_SectionMood");
    public static string MoodNote => L.GetString("Report_MoodNote");
    public static string MoodChartLabel => L.GetString("Report_MoodChartLabel");
    public static string SectionWater => L.GetString("Report_SectionWater");
    public static string SectionAppetite => L.GetString("Report_SectionAppetite");
    public static string MeasuredAndObservedNote => L.GetString("Report_MeasuredAndObservedNote");
    public static string Measured => L.GetString("Report_Measured");
    public static string OwnerObservations => L.GetString("Report_OwnerObservations");
    public static string Subjective => L.GetString("Report_Subjective");
    public static string FoodsRecorded => L.GetString("Report_FoodsRecorded");

    /// <summary>Mood chart rows, level 1 (bottom) → 5 (top). The one scale with its own
    /// report keys instead of the journal's: the app's Mood_* words are adjectives meant
    /// to sit inside a sentence, so German writes them lowercase ("war heute gedämpft").
    /// A chart axis is not a sentence — same five levels, cased to stand alone.</summary>
    public static string[] MoodRows => LevelRows("Report_MoodLevel");

    /// <summary>Water observation rows, level 1 → 5. Same keys the water sheet uses.</summary>
    public static string[] WaterRows => LevelRows("Water_Level");

    /// <summary>Appetite observation rows, level 1 → 5. Same keys the appetite sheet uses.</summary>
    public static string[] AppetiteRows => LevelRows("Appetite_Level");

    // ── Events ────────────────────────────────────────────────────────────────
    public static string SectionEvents => L.GetString("Report_SectionEvents");
    public static string ColDate => L.GetString("Report_ColDate");
    public static string ColTime => L.GetString("Report_ColTime");
    public static string ColEvent => L.GetString("Report_ColEvent");
    public static string ColDetails => L.GetString("Report_ColDetails");
    public static string EventSeizure => L.GetString("Report_EventSeizure");
    public static string EventVomiting => L.GetString("Report_EventVomiting");
    public static string EventLowAppetite => L.GetString("Report_EventLowAppetite");
    public static string EventDuration(int minutes) => L.Format("Report_EventDuration", minutes);
    public static string EventAppetiteLevel(int level) => L.Format("Report_EventAppetiteLevel", level);
    public static string MoreEvents(int count) => L.Format("Report_MoreEvents", count);

    // ── Notes ─────────────────────────────────────────────────────────────────
    public static string SectionNotes => L.GetString("Report_SectionNotes");
    public static string MoreNotes(int count) => L.Format("Report_MoreNotes", count);

    /// <summary>Placeholder for a cell with nothing recorded. Not a localized word —
    /// an em dash reads the same in every language the app ships.</summary>
    public const string Empty = "—";

    private static string[] LevelRows(string keyPrefix) => new[]
    {
        L.GetString(keyPrefix + "1"), L.GetString(keyPrefix + "2"), L.GetString(keyPrefix + "3"),
        L.GetString(keyPrefix + "4"), L.GetString(keyPrefix + "5")
    };
}
