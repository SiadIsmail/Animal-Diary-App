namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using System.Linq;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Controls.Shapes;

// This file's own namespace is ...Data.View, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel vm;
    private CalendarPage? calendarPage;
    private PetsPage? petPage;
    private int _toastSeq;
    private DoseItem? _nextDose;

    public MainPage(MainViewModel mainViewModel)
    {
        InitializeComponent();
        vm = mainViewModel;
        BindingContext = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        vm.SettingsVM.ConfirmDeleteAllData = () =>
            DisplayAlert(
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmTitle"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmMessage"),
                LocalizationManager.Instance.GetString("Settings_DeleteConfirmAccept"),
                LocalizationManager.Instance.GetString("Common_Cancel"));

        vm.SettingsVM.ResetCompleted += OnResetCompleted;

        // Keep the care ring and next-up card in step with today's data.
        vm.CalendarVM.PropertyChanged += OnCalendarVmPropertyChanged;

        await vm.LoadAsync();
        await vm.MainPageVM.LoadWeightChartAsync();
        await vm.MainPageVM.LoadMoodTimelineAsync();

        SetAside();
        RefreshRing();
        RefreshNextMed();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        vm.SettingsVM.ConfirmDeleteAllData = null;
        vm.SettingsVM.ResetCompleted -= OnResetCompleted;
        vm.CalendarVM.PropertyChanged -= OnCalendarVmPropertyChanged;
    }

    private void OnResetCompleted(object? sender, EventArgs e)
    {
        // Clear in-memory form drafts so stale inputs don't survive the wipe
        // (the ViewModels are singletons).
        vm.ResetDrafts();
        Application.Current!.Windows[0].Page = new NavigationPage(new WelcomePage(vm));
    }

    async void OnCalendarClicked(object? sender, EventArgs args)
    {
        calendarPage ??= new CalendarPage(vm);
        await Navigation.PushAsync(calendarPage);
    }

    async void OnPetsClicked(object? sender, EventArgs args)
    {
        petPage ??= new PetsPage(vm);
        await Navigation.PushAsync(petPage);
    }

    private void OnCalendarVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // These derived flags fire whenever the day's entries/doses reload.
        if (e.PropertyName is nameof(CalendarViewModel.AllCareComplete)
            or nameof(CalendarViewModel.HasMood)
            or nameof(CalendarViewModel.HasWeight)
            or nameof(CalendarViewModel.IsSelectedDateToday))
        {
            RefreshRing();
            RefreshNextMed();
        }
    }

    // ── Handwritten aside under the greeting (Caveat, rotated per load) ──
    private void SetAside()
    {
        var pet = vm.MainPageVM.ActivePet?.Name ?? string.Empty;
        var keys = new[] { "Main_Aside1", "Main_Aside2", "Main_Aside3" };
        var key = keys[Random.Shared.Next(keys.Length)];
        AsideLabel.Text = LocalizationManager.Instance.Format(key, pet);
    }

    // ── Care-completion ring: today's mood + weight + doses (DATA) ──
    private void RefreshRing() => CareRing.Progress = ComputeCareProgress();

    private double ComputeCareProgress()
    {
        var c = vm.CalendarVM;
        // Only meaningful for today; a parked non-today selection reads as empty.
        if (!c.IsSelectedDateToday)
            return 0;

        int total = 2 + c.DosesForSelectedDate.Count;           // mood + weight + doses
        int done = (c.HasMood ? 1 : 0)
                 + (c.HasWeight ? 1 : 0)
                 + c.DosesForSelectedDate.Count(d => d.IsTaken);
        return total == 0 ? 0 : (double)done / total;
    }

    // ── Next-up medication card ──
    private void RefreshNextMed()
    {
        var c = vm.CalendarVM;
        _nextDose = c.IsSelectedDateToday
            ? c.DosesForSelectedDate.FirstOrDefault(d => !d.IsTaken && !d.IsSkipped)
            : null;

        var loc = LocalizationManager.Instance;
        if (_nextDose is { } dose)
        {
            NextMedIcon.Text = "💊";
            NextMedName.Text = dose.MedName;
            NextMedDetail.Text = dose.DoseDisplay;
            NextMedTime.Text = dose.TimeDisplay;
            NextMedTime.IsVisible = true;
            NextMedAction.IsVisible = dose.IsPending;
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

    private void OnMarkNextMedGiven(object? sender, EventArgs e)
    {
        if (_nextDose is not { } dose)
            return;

        // The toggle command records the outcome asynchronously, so refresh the
        // ring + card only once the dose actually flips to Taken.
        void OnDoseResolved(object? s, PropertyChangedEventArgs args)
        {
            if (args.PropertyName is not (nameof(DoseItem.Status) or nameof(DoseItem.IsTaken)))
                return;
            dose.PropertyChanged -= OnDoseResolved;
            MainThread.BeginInvokeOnMainThread(() =>
            {
                RefreshNextMed();
                RefreshRing();
            });
        }

        dose.PropertyChanged += OnDoseResolved;
        vm.CalendarVM.ToggleDoseTakenCommand.Execute(dose);

        // Immediate, optimistic confirmation feedback.
        ShowToast(MedGivenToast());
        if (sender is View anchor)
            _ = BurstBubblesAsync(anchor);
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
