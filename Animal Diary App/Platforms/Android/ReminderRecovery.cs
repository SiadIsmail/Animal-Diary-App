using Android.App;
using Android.App.Job;
using Android.Content;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Data.Device;
using Animal_Diary_App.Data.Services.Notifications;
using Animal_Diary_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui;

namespace Animal_Diary_App;

/// <summary>
/// Shared entry point for the Android broadcast receivers that re-arm reminders.
///
/// The receivers do NOT do the work themselves. A broadcast receiver gets roughly ten
/// seconds before the system treats it as unresponsive, and <c>goAsync()</c> does not
/// extend that budget: it only moves the work off the main thread. Re-arming can mean
/// resolving a backlog, re-materializing hundreds of occurrences, running the 14-day
/// dose reconcile and refreshing every pet's daily reminder, which will overrun that
/// window on a real device mid-boot. Being killed halfway is the worst possible outcome:
/// pending alarms have already been cancelled and their replacements never armed.
///
/// So the receiver only <see cref="Enqueue"/>s a <see cref="ReminderRecoveryJobService"/>
/// job (a few milliseconds) and returns; the job then runs with a proper budget and is
/// re-run by the system if it gets killed.
/// </summary>
internal static class ReminderRecovery
{
    // Any stable, app-unique id. Re-scheduling with the same id replaces a job that
    // hasn't run yet, so a boot storm can't queue the work twice.
    private const int JobId = 4711;

    /// <summary>
    /// Hand recovery off to the job scheduler. <paramref name="isBootRecovery"/> records
    /// (durably, before any async work) that doses missed while the device was off
    /// still need re-sending; the catch-up reads that flag rather than taking it as a
    /// parameter, so the intent survives whichever entry point ends up running first.
    /// </summary>
    public static void Enqueue(BroadcastReceiver receiver, Context? context, bool isBootRecovery)
    {
        if (isBootRecovery)
        {
            try { MedicationReminderScheduler.MarkBootRecoveryPending(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Reminders] boot flag failed: {ex.Message}"); }
        }

        if (TrySchedule(context))
            return;

        // Job scheduling unavailable: fall back to doing it inline, which is what the
        // app did before. Better a pass that might be cut short than no pass at all.
        RunInline(receiver);
    }

    private static bool TrySchedule(Context? context)
    {
        try
        {
            var ctx = context ?? Android.App.Application.Context;
            if (ctx.GetSystemService(Context.JobSchedulerService) is not JobScheduler scheduler)
                return false;

            var component = new ComponentName(ctx, Java.Lang.Class.FromType(typeof(ReminderRecoveryJobService)));
            var builder = new JobInfo.Builder(JobId, component);
            if (builder is null)
                return false;

            // Run as soon as the system is willing; a deadline is required for a job
            // with no other constraints.
            var info = builder
                .SetOverrideDeadline(1_000)
                ?.SetPersisted(false)
                ?.Build();

            if (info is null)
                return false;

            return scheduler.Schedule(info) == JobScheduler.ResultSuccess;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reminders] job scheduling failed: {ex.Message}");
            return false;
        }
    }

    private static void RunInline(BroadcastReceiver receiver)
    {
        var pending = receiver.GoAsync();
        Task.Run(async () =>
        {
            try { await RunWorkAsync(); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            finally { pending?.Finish(); }
        }).Forget();
    }

    /// <summary>
    /// The actual recovery pass. Safe to call from the job service or inline.
    /// <c>resendMissed: false</c> is passed deliberately: the durable boot flag set in
    /// <see cref="Enqueue"/> is what decides whether missed doses get re-sent.
    /// </summary>
    public static async Task RunWorkAsync()
    {
        var services = IPlatformApplication.Current?.Services;
        if (services is null)
            return;

        var database = services.GetService<AppDatabase>();
        if (database is not null)
            await database.EnsureInitializedAsync();

        // Notification copy is built from the localized resources at scheduling time.
        // In a boot-started process the saved language may not have been applied yet
        // (that happens on the normal startup path, which races this one), so a German
        // carer could get an entire horizon of English reminders. Apply it first.
        await ApplySavedLanguageAsync(services);

        // Channels must exist before anything is posted to them.
        var notifications = services.GetService<INotificationService>();
        if (notifications is not null)
            await notifications.EnsureChannelsAsync();

        var scheduler = services.GetService<MedicationReminderScheduler>();
        if (scheduler is not null)
            await scheduler.CatchUpAndRefreshAsync(resendMissed: false);

        // Re-arm today's daily care reminder too (a reboot clears the OS alarm).
        var dailyScheduler = services.GetService<DailyCareReminderScheduler>();
        if (dailyScheduler is not null)
            await dailyScheduler.RefreshAsync();

        // And the one reminder before a vet visit: same reason: a reboot clears the
        // OS alarm, and this one-shot may be days out.
        var appointmentScheduler = services.GetService<AppointmentReminderScheduler>();
        if (appointmentScheduler is not null)
            await appointmentScheduler.RefreshAsync();
    }

    private static async Task ApplySavedLanguageAsync(IServiceProvider services)
    {
        try
        {
            var settings = services.GetService<SettingsService>();
            if (settings is null)
                return;

            var saved = await settings.GetLanguageAsync();

            // Only when it differs: re-applying raises "all bindings changed", which we
            // have no business doing from a background thread if the UI is alive.
            if (!string.IsNullOrEmpty(saved) && LocalizationManager.Instance.CurrentLanguage != saved)
                LocalizationManager.Instance.SetLanguage(saved);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Reminders] language apply failed: {ex.Message}");
        }
    }
}

/// <summary>
/// Runs the reminder recovery pass off the broadcast receiver's ~10-second budget.
/// The system gives a job a far more generous window and will re-run it if the process
/// is killed mid-pass (<see cref="OnStopJob"/> returns true to request that).
/// </summary>
[Service(Name = "com.felova.app.ReminderRecoveryJobService",
         Permission = "android.permission.BIND_JOB_SERVICE",
         Exported = false)]
public class ReminderRecoveryJobService : JobService
{
    public override bool OnStartJob(JobParameters? parameters)
    {
        Task.Run(async () =>
        {
            try
            {
                await ReminderRecovery.RunWorkAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Reminders] recovery job failed: {ex.Message}");
            }
            finally
            {
                // false: the pass is complete either way. A genuinely interrupted pass
                // comes back through OnStopJob's reschedule, and the next app launch
                // re-runs the same idempotent catch-up regardless.
                try { JobFinished(parameters, false); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Reminders] JobFinished failed: {ex.Message}"); }
            }
        }).Forget();

        return true;    // work continues on another thread
    }

    public override bool OnStopJob(JobParameters? parameters) => true;   // please re-run
}
