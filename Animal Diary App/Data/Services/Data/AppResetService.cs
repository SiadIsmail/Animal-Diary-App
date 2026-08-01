namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// The "delete all data" path. Invariant: this must wipe EVERY table created in
/// <c>AppDatabase.InitAsync</c>, cancel every notification armed with the OS, and
/// clear persisted scheduler state — health data is medical data, and nothing may
/// survive a reset the user asked for.
///
/// <para>The table half of that invariant is now structural: both this method and
/// <c>AppDatabase</c> iterate <see cref="SyncedTables.Everything"/>, so they cannot
/// disagree about what exists. What still needs care is everything that is
/// <b>not</b> a table — files on disk, <c>Preferences</c> keys, in-memory caches —
/// which is what the rest of this method is.</para>
/// </summary>
public class AppResetService
{
    private readonly AppDatabase _db;
    private readonly ActivePetService _activePetService;
    private readonly INotificationService _notifications;
    private readonly Reports.ReportLibraryService _reportLibrary;
    private readonly PetPhotoService _petPhotos;

    public AppResetService(AppDatabase db, ActivePetService activePetService, INotificationService notifications,
        Reports.ReportLibraryService reportLibrary, PetPhotoService petPhotos)
    {
        _db = db;
        _activePetService = activePetService;
        _notifications = notifications;
        _reportLibrary = reportLibrary;
        _petPhotos = petPhotos;
    }

    public async Task ResetDataAsync()
    {
        // Already-armed one-shot reminders would otherwise keep firing for up to
        // 14 days, naming the deleted pet and medication.
        await _notifications.CancelAllNotifications();

        // Every table the app creates, from the one registry — including the
        // device-local ones. A new table is a line in SyncedTables, never an edit
        // here, which is what makes "a reset wipes everything" hold by construction.
        foreach (var table in SyncedTables.Everything)
            await table.DeleteEveryRowAsync(_db.Connection);

        // Reports are files + rows; the library wipes both (health data is medical
        // data — a reset must not leave PDFs behind in app storage).
        await _reportLibrary.DeleteAllAsync();

        // Pet photos are files with no table of their own; wipe the whole folder.
        _petPhotos.DeleteAll();

        // Drop the in-memory cloud diagnostics buffer too (technical logs only,
        // but a wipe should leave nothing behind).
        Animal_Diary_App.Data.Services.Cloud.CloudDiagnostics.Clear();

        // Forget the reminder catch-up marker so a fresh start can't misread the
        // old install's "last seen" time.
        MedicationReminderScheduler.ClearPersistedState();

        // Forget per-device pause state (AI/app-voice.md §15) so a fresh start begins
        // with every pet active, not silently paused from the wiped install.
        PetPauseService.ClearPersistedState();

        // Forget the daily care reminder opt-in + time (device-local).
        Notifications.DailyCareReminderSettings.ClearPersistedState();

        // Privacy: a data wipe also throws away the anonymous analytics id and mints a
        // fresh one, so a reset install starts a brand-new anonymous identity — past
        // events can no longer be associated with the new one. No personal data is
        // involved (events never carry any), but this keeps the reset total.
        Analytics.AnalyticsIdentity.Rotate();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _activePetService.ActivePet = new Pet();
        });
    }
}
