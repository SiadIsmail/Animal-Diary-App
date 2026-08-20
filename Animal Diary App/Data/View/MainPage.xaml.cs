namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Controls.Shapes;

// This file's own namespace is ...Data.View, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel vm;
    private int _toastSeq;

    /// <summary>The overlay sheets are queued for background building once, after the
    /// first load settles. Re-queuing on every remote-change reload would only enqueue
    /// no-ops (Realise is idempotent), but the flag keeps the intent obvious.</summary>
    private bool _sheetsQueued;

    /// <summary>What the last completed load was a load of, and when. Together they
    /// decide whether an appearance has to hit the database at all.</summary>
    /// <summary>The data version is read BEFORE the load and stamped AFTER it, never
    /// re-read at the end. A cloud pull that lands mid-load bumps the version and posts
    /// its own reload; stamping the post-load value would make that reload look
    /// redundant and skip it, leaving the page a caregiver's entry behind. Reading it
    /// first can only cause one extra reload, which is the harmless direction.</summary>
    private PageLoadKey _loaded;
    private DateTime _loadedAtUtc = DateTime.MinValue;

    public MainPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    // Android back closes an open sheet (or the settings panel) before it navigates.
    protected override bool OnBackButtonPressed()
        => Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Only the visible tab animates its backdrop — all three pages stay alive for
        // the Shell's lifetime, so a self-starting background ran three copies forever.
        Backdrop.Start();

        vm.DevVM.ImportRequested += OnImportRequested;

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmMessage"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        // Signed-in reset is a choice: keep the backup or destroy it too. Three outcomes,
        // so it uses the confirm sheet rather than the native action sheet.
        vm.SettingsVM.ConfirmDeleteAllDataCloud = () => ResetScopePrompt.AskAsync(vm.ConfirmVM);

        vm.CloudVM.ConfirmDeleteAccount = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmTitle"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmMessage"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        // No export sheet on this page, so no "save a copy" option here.
        vm.CloudVM.ConfirmSignOut = impact => SignOutPrompt.AskAsync(this, impact, null, null);
        vm.CloudVM.SignedOut += OnSignedOut;

        vm.SettingsVM.ResetCompleted += OnResetCompleted;
        // Another caregiver's changes landing while this page is visible reload
        // it in place — no tab-switching needed to see them.
        vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;
        // The owner changed what a stat card shows: re-read both (a pick can swap
        // the pair, so neither card can be refreshed on its own).
        vm.TodayCardSheetVM.Changed += OnStatCardsChanged;
        vm.MainPageVM.AppointmentRequested += OnAppointmentRequested;

        await ReloadDataAsync();
    }

    /// <summary>The page's full data load — runs on every appearance AND when a
    /// cloud sync applies remote changes while the page is visible.</summary>
    private async Task ReloadDataAsync()
    {
        try
        {
            // Always, however fresh the page is: the reminder health probe is a LIVE
            // read of an OS permission that can be revoked while the app isn't running,
            // and a cached answer there is the one failure this product least wants
            // (see MainPageViewModel). The greeting comes from the clock, not the
            // database, so it follows the hour rolling past noon or 6pm.
            vm.MainPageVM.RefreshClockDerived();
            await vm.MainPageVM.RefreshReminderHealthAsync();

            // Always, for the same reason: "within seven days" moves by itself at
            // midnight, so a cached answer would leave the band up a day too long or
            // miss the day it should appear.
            await vm.MainPageVM.RefreshNearVisitAsync();

            // Nothing has changed since this page last loaded, and that was moments
            // ago: the queries below would repaint identical pixels. Keyed on the
            // active pet and the day as well as the data version, because switching
            // pets writes no row and neither does midnight passing. See DataVersion
            // for why the freshness window is part of the guard, not a nicety.
            var version = DataVersion.Current;
            var key = new PageLoadKey(
                version, vm.MainPageVM.ActivePet?.Id ?? 0, DateTime.Now.Date);
            if (key == _loaded && DateTime.UtcNow - _loadedAtUtc < PageLoadKey.Freshness)
            {
                // Still re-derive the next-up card: whether its action button is
                // showing depends on the time of day, not on anything stored.
                RefreshNextUp();
                return;
            }

            await vm.LoadAsync();

            // One at a time, not Task.WhenAll. These all end in sqlite-net, whose async
            // API queues each query to the thread pool and then serializes them on the
            // one shared connection — so fanning out occupied four pooled threads to run
            // one query and gained nothing. Same wall time, one thread.
            // One section now, reading the pet's care plan, where two hardcoded
            // surfaces (a mood ribbon and a weight chart) used to be.
            await vm.LookBackVM.LoadAsync();
            // Both customizable stat cards: which records they hold and the
            // pet's latest value for each.
            await vm.MainPageVM.LoadStatCardsAsync();
            // Drives the care ring (bound) + next-up card, so logs made on other tabs
            // are reflected the moment this page returns.
            await vm.MainPageVM.LoadTodayCareAsync();

            SetAside();
            RefreshNextUp();

            // The pet and the day are re-read (the load can switch the active pet); the
            // version is the one captured before it started — see the field's note.
            _loaded = new PageLoadKey(
                version, vm.MainPageVM.ActivePet?.Id ?? 0, DateTime.Now.Date);
            _loadedAtUtc = DateTime.UtcNow;

            // The sheets can be built now that the page has its data: deferred to here
            // deliberately, so their inflation never competes with the load the person
            // is actually waiting for. See Controls/SheetHost.cs.
            if (!_sheetsQueued)
            {
                _sheetsQueued = true;
                Controls.SheetHost.PreloadAll(this);
            }
        }
        catch (Exception ex)
        {
            // A failed load must degrade to an empty page, never crash the app
            // (async void callers — an escaping exception here kills the process).
            System.Diagnostics.Debug.WriteLine($"[MainPage] reload failed: {ex}");
        }
    }

    // Signing out removed this account's pets from the device. If nothing is left, the app
    // has nothing to show — route to onboarding exactly as deleting the last pet does.
    // Otherwise reload in place; local-only pets can still be here.
    private async void OnSignedOut(bool anyPetsRemain)
    {
        try
        {
            if (!anyPetsRemain)
            {
                (Application.Current as App)?.SwitchToOnboarding();
                return;
            }
            await vm.LoadAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cloud] after-sign-out refresh failed: {ex.Message}");
        }
    }

    private void OnRemoteChangesApplied() =>
        MainThread.BeginInvokeOnMainThread(async () => await ReloadDataAsync());

    private async void OnStatCardsChanged()
    {
        try
        {
            await vm.MainPageVM.LoadStatCardsAsync();
        }
        catch (Exception ex)
        {
            // async void — an escaping exception here kills the process.
            System.Diagnostics.Debug.WriteLine($"[MainPage] stat card reload failed: {ex}");
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        Backdrop.Stop();
        vm.SettingsVM.ConfirmDeleteAllData = null;
        vm.SettingsVM.ConfirmDeleteAllDataCloud = null;
        vm.CloudVM.ConfirmDeleteAccount = null;
        vm.CloudVM.ConfirmSignOut = null;
        vm.CloudVM.SignedOut -= OnSignedOut;
        vm.SettingsVM.ResetCompleted -= OnResetCompleted;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
        vm.TodayCardSheetVM.Changed -= OnStatCardsChanged;
        vm.MainPageVM.AppointmentRequested -= OnAppointmentRequested;
        vm.DevVM.ImportRequested -= OnImportRequested;
    }

    // The band on Today is a door to the appointment page. The VM raises; the page
    // pushes — the same split every other pushed page here uses.
    private async void OnAppointmentRequested()
    {
        try
        {
            await Navigation.PushAsync(new AppointmentPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[MainPage] appointment push failed: {ex}");
        }
    }

    private void OnResetCompleted(object? sender, EventArgs e)
    {
        // Clear in-memory form drafts so stale inputs don't survive the wipe
        // (the ViewModels are singletons).
        vm.ResetDrafts();
        // Through App: it records the "no pets" state so an Activity recreation later
        // rebuilds onboarding rather than the Shell, and it targets the live window.
        (Application.Current as App)?.SwitchToOnboarding();
    }

    // ── Handwritten aside under the greeting (Caveat, rotated per load) ──
    private void SetAside()
    {
        var pet = vm.MainPageVM.ActivePet?.Name ?? string.Empty;
        var keys = new[] { "Main_Aside1", "Main_Aside2", "Main_Aside3" };
        var key = keys[Random.Shared.Next(keys.Length)];
        AsideLabel.Text = LocalizationManager.Instance.Format(key, pet);
    }

    // ── Next-up card: the first thing still to do today ──
    // Med doses first (soonest due), then care-plan trackers — the same
    // PendingEngine order the Journal's chips use (MainPageViewModel supplies it).
    private void RefreshNextUp()
    {
        var loc = LocalizationManager.Instance;
        var item = vm.MainPageVM.NextUpItem;

        if (item is { Kind: PendingKind.Medication })
        {
            NextMedIcon.Text = "💊";
            NextMedName.Text = item.MedicationName;
            NextMedDetail.Text = vm.MainPageVM.NextUpDetail;
            NextMedTime.Text = item.DoseTime?.ToString(@"hh\:mm") ?? string.Empty;
            NextMedTime.IsVisible = item.DoseTime.HasValue;
            NextMedAction.Text = loc.GetString("Journal_MarkGiven");
            // You can't take a dose early — the action appears once it's due.
            NextMedAction.IsVisible = (item.DoseTime ?? TimeSpan.Zero) <= DateTime.Now.TimeOfDay;
        }
        else if (item is { Kind: PendingKind.Tracker })
        {
            var (icon, label) = TrackerDisplay(item, loc);
            NextMedIcon.Text = icon;
            NextMedName.Text = label;
            // PerDay trackers (glucose) show their "1 of 3" count; others need no detail.
            NextMedDetail.Text = item.Target > 0
                ? loc.Format("Journal_CountOfN", item.Done, item.Target)
                : string.Empty;
            NextMedTime.IsVisible = false;
            NextMedAction.Text = loc.GetString("Main_LogNow");
            NextMedAction.IsVisible = true;
        }
        else
        {
            NextMedIcon.Text = "✓";
            NextMedName.Text = loc.GetString("Main_AllCaughtUpTitle");
            NextMedDetail.Text = loc.GetString("Main_AllCaughtUpDetail");
            NextMedTime.IsVisible = false;
            NextMedAction.IsVisible = false;
        }
    }

    // Same icons + labels as the Journal's chips — literally the same table — so the
    // card reads as the first chip of the day. Tapping a tracker card routes to the
    // Journal, where every tracker (water included) now has its logging sheet.
    private (string Icon, string Label) TrackerDisplay(PendingItem item, LocalizationManager loc)
    {
        // An owner-defined tracker's name and emoji are its own row's, resolved by the VM
        // (this page reads no store). The whole branch is handled here, including the
        // unresolved case: falling THROUGH to TrackerVisuals would show a walk as "Mood",
        // because the shared fallback's label key is the mood one.
        if (item.Tracker is { IsCustom: true })
        {
            return vm.MainPageVM.NextUpCustom is { } own
                ? (CustomTrackerVisuals.For(own).Icon, own.Name)
                : (CustomTrackerVisuals.DefaultIcon, loc.GetString("Today_CardCustom"));
        }

        var v = TrackerVisuals.For(item.Tracker);
        return (v.Icon, loc.GetString(v.LabelKey));
    }

    private async void OnNextUpAction(object? sender, EventArgs e)
    {
        try
        {
            if (vm.MainPageVM.NextUpItem is { Kind: PendingKind.Medication })
            {
                // Immediate, optimistic confirmation feedback; the awaited call
                // then records the dose and re-derives the ring + next-up.
                ShowToast(MedGivenToast());
                if (sender is View anchor)
                    BurstBubblesAsync(anchor).Forget();

                await vm.MainPageVM.MarkNextDoseGivenAsync();
                RefreshNextUp();
            }
            else if (vm.MainPageVM.NextUpItem is { Kind: PendingKind.Tracker })
            {
                // Logging sheets live on the Journal — take the person there.
                await Shell.Current.GoToAsync("//JournalTab");
            }
        }
        catch (Exception ex)
        {
            // async void — an escaping exception here kills the process.
            System.Diagnostics.Debug.WriteLine($"[MainPage] Next-up action failed: {ex}");
        }
    }

    // ── Rotating warm copy (shared bank with Journal) ──
    private string MedGivenToast()
    {
        var keys = new[] { "Toast_MedGiven1", "Toast_MedGiven2", "Toast_MedGiven3" };
        var key = keys[Random.Shared.Next(keys.Length)];
        return LocalizationManager.Instance.Format(key, vm.MainPageVM.ActivePet?.Name ?? string.Empty);
    }

    // ── Toast overlay (same treatment as Journal) ──
    private async void ShowToast(string message)
    {
        ToastLabel.Text = message;
        int seq = ++_toastSeq;

        if (ReducedMotion.IsEnabled)
        {
            Toast.TranslationY = 0;
            Toast.Opacity = 1;
            await Task.Delay(2400);
            if (seq == _toastSeq)
                Toast.Opacity = 0;
            return;
        }

        Toast.TranslationY = 16;
        Toast.Opacity = 0;
        await Task.WhenAll(
            Toast.FadeTo(1, 220, Easing.CubicOut),
            Toast.TranslateTo(0, 0, 220, Easing.CubicOut));

        await Task.Delay(2400);
        if (seq != _toastSeq)
            return;
        await Toast.FadeTo(0, 260, Easing.CubicIn);
    }

    // ── Reusable confirm burst (shared with Journal) ──
    private async Task BurstBubblesAsync(View anchor)
    {
        if (ReducedMotion.IsEnabled || anchor.Width <= 0)
            return;

        var origin = GetPositionInPage(anchor);
        double cx = origin.X + anchor.Width / 2;
        double cy = origin.Y + anchor.Height / 2;
        var rng = Random.Shared;

        var tasks = new List<Task>();
        for (int i = 0; i < 9; i++)
        {
            double size = 6 + rng.NextDouble() * 12;
            var bubble = new Ellipse
            {
                WidthRequest = size,
                HeightRequest = size,
                InputTransparent = true,
                StrokeThickness = 1.2,
                Stroke = new SolidColorBrush(Color.FromArgb("#BFFFFFFF")),
                Fill = new RadialGradientBrush
                {
                    Center = new Point(0.35, 0.3),
                    Radius = 0.7,
                    GradientStops = new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb("#E6FFFFFF"), 0f),
                        new GradientStop(Color.FromArgb("#40FFFFFF"), 0.6f),
                        new GradientStop(Colors.Transparent, 1f),
                    }
                },
                Opacity = 0
            };

            double left = cx - size / 2 + (rng.NextDouble() * 36 - 18);
            double top = cy - size / 2;
            AbsoluteLayout.SetLayoutBounds(bubble, new Rect(left, top, size, size));
            EffectLayer.Add(bubble);

            double dx = rng.NextDouble() * 44 - 22;
            double dy = -(46 + rng.NextDouble() * 56);
            uint dur = (uint)(700 + rng.NextDouble() * 600);
            tasks.Add(AnimateBubbleAsync(bubble, dx, dy, dur));
        }

        await Task.WhenAll(tasks);
    }

    private async Task AnimateBubbleAsync(Ellipse bubble, double dx, double dy, uint durationMs)
    {
        await bubble.FadeTo(1, durationMs / 5, Easing.CubicOut);
        await Task.WhenAll(
            bubble.TranslateTo(dx, dy, durationMs, Easing.CubicOut),
            bubble.FadeTo(0, durationMs, Easing.CubicIn));
        EffectLayer.Remove(bubble);
    }

    private Point GetPositionInPage(VisualElement element)
    {
        double x = 0, y = 0;
        Element? current = element;
        while (current is VisualElement ve && !ReferenceEquals(current, Content))
        {
            x += ve.X;
            y += ve.Y;
            if (ve.Parent is ScrollView scroll)
            {
                x -= scroll.ScrollX;
                y -= scroll.ScrollY;
            }
            current = ve.Parent;
        }
        return new Point(x, y);
    }

    // The dev sheet is a ContentView and cannot navigate, so the hosting page pushes the
    // importer on its behalf (same shape as the export sheet's ViewRequested below).
    private async void OnImportRequested()
    {
        try
        {
            await Navigation.PushAsync(new ImportPage(vm));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Import] push failed: {ex}");
        }
    }

}
