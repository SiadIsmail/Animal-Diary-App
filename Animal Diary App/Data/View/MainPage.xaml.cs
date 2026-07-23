namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Models;
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

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmMessage"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        // Signed-in reset is a choice: keep the backup or destroy it too.
        vm.SettingsVM.ConfirmDeleteAllDataCloud = async () =>
        {
            var deviceOnly = LocalizationManager.Instance.GetString("Settings_ResetDeviceOnly");
            var everything = LocalizationManager.Instance.GetString("Settings_ResetEverything");
            var choice = await DisplayActionSheet(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Common_Cancel"),
                everything,
                deviceOnly);
            if (choice == deviceOnly) return Data.ViewModels.ResetScope.DeviceOnly;
            if (choice == everything) return Data.ViewModels.ResetScope.Everything;
            return null;
        };

        vm.CloudVM.ConfirmDeleteAccount = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmTitle"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmMessage"),
                LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        vm.SettingsVM.ResetCompleted += OnResetCompleted;
        // Another caregiver's changes landing while this page is visible reload
        // it in place — no tab-switching needed to see them.
        vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;

        await ReloadDataAsync();
    }

    /// <summary>The page's full data load — runs on every appearance AND when a
    /// cloud sync applies remote changes while the page is visible.</summary>
    private async Task ReloadDataAsync()
    {
        try
        {
            await vm.LoadAsync();
            // The charts and the today-care snapshot are independent of each
            // other; overlap their queries instead of running them back-to-back.
            // LoadTodayCareAsync drives the care ring (bound) + next-up card and
            // re-runs on every appearance, so logs made on other tabs are
            // reflected the moment this page returns.
            await Task.WhenAll(
                vm.MainPageVM.LoadWeightChartAsync(),
                vm.MainPageVM.LoadMoodTimelineAsync(),
                vm.MainPageVM.LoadLatestMoodAsync(),
                vm.MainPageVM.LoadTodayCareAsync());

            SetAside();
            RefreshNextUp();
        }
        catch (Exception ex)
        {
            // A failed load must degrade to an empty page, never crash the app
            // (async void callers — an escaping exception here kills the process).
            System.Diagnostics.Debug.WriteLine($"[MainPage] reload failed: {ex}");
        }
    }

    private void OnRemoteChangesApplied() =>
        MainThread.BeginInvokeOnMainThread(async () => await ReloadDataAsync());

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vm.SettingsVM.ConfirmDeleteAllData = null;
        vm.SettingsVM.ConfirmDeleteAllDataCloud = null;
        vm.CloudVM.ConfirmDeleteAccount = null;
        vm.SettingsVM.ResetCompleted -= OnResetCompleted;
        vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
    }

    private void OnResetCompleted(object? sender, EventArgs e)
    {
        // Clear in-memory form drafts so stale inputs don't survive the wipe
        // (the ViewModels are singletons).
        vm.ResetDrafts();
        Application.Current!.Windows[0].Page = new NavigationPage(new WelcomePage(vm));
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

    // Same icons + labels as the Journal's chips, so the card reads as the
    // first chip of the day. Tapping a tracker card routes to the Journal, where
    // every tracker (water included) now has its logging sheet.
    private static (string Icon, string Label) TrackerDisplay(PendingItem item, LocalizationManager loc) => item.TrackerId switch
    {
        TrackerId.Glucose => ("🩸", loc.GetString("Journal_GlucoseCheck")),
        TrackerId.Appetite => ("🍽️", loc.GetString("Journal_Appetite")),
        TrackerId.Weight => ("⚖️", loc.GetString("Journal_WeighIn")),
        TrackerId.Seizure => ("⚡", loc.GetString("Journal_Seizure")),
        TrackerId.Water => ("💧", loc.GetString("Journal_Water")),
        _ => ("🙂", loc.GetString("Journal_MoodTitle")),
    };

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
}
