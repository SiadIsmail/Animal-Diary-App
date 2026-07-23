namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Notifications;

/// <summary>
/// The "delete all data" path. Invariant: this must wipe EVERY table created in
/// <c>AppDatabase.InitAsync</c> (add new tables here in the same commit), cancel
/// every notification armed with the OS, and clear persisted scheduler state —
/// health data is medical data, and nothing may survive a reset the user asked for.
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

        await _db.Connection.DeleteAllAsync<Pet>();
        await _db.Connection.DeleteAllAsync<PetEntry>();
        await _db.Connection.DeleteAllAsync<Medication>();
        await _db.Connection.DeleteAllAsync<AppSettings>();
        await _db.Connection.DeleteAllAsync<MedicationSchedule>();
        await _db.Connection.DeleteAllAsync<ReminderInstance>();
        await _db.Connection.DeleteAllAsync<MedicationDoseLog>();
        await _db.Connection.DeleteAllAsync<Tracker>();
        await _db.Connection.DeleteAllAsync<PetCondition>();
        await _db.Connection.DeleteAllAsync<GlucoseEntry>();
        await _db.Connection.DeleteAllAsync<AppetiteEntry>();
        await _db.Connection.DeleteAllAsync<AppetiteAmountEntry>();
        await _db.Connection.DeleteAllAsync<SeizureEntry>();
        await _db.Connection.DeleteAllAsync<WaterAmountEntry>();
        await _db.Connection.DeleteAllAsync<WaterLevelEntry>();
        await _db.Connection.DeleteAllAsync<SyncState>();

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
