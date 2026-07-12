namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Controls.Shapes;

// This file's own namespace is ...Data.View, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

public partial class CalendarPage : ContentPage
{
	private MainViewModel vm;
	private int _toastSeq;

	// The undo behind the currently-shown toast (null when the toast has no undo).
	private Func<Task>? _pendingUndo;

	public CalendarPage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	protected override async void OnAppearing()
	{
		base.OnAppearing();

		vm.CalendarVM.PropertyChanged += OnCalendarVmPropertyChanged;
		vm.CalendarVM.TrackingChanged += OnTrackingChanged;
		vm.JournalVM.PropertyChanged += OnJournalVmPropertyChanged;
		vm.JournalVM.RequestOpenSheet += OnRequestOpenSheet;
		vm.GlucoseSheetVM.Saved += OnSheetSaved;
		vm.MoodSheetVM.Saved += OnSheetSaved;
		vm.WeightSheetVM.Saved += OnSheetSaved;
		vm.AppetiteSheetVM.Saved += OnSheetSaved;

		if (vm.CalendarVM.Pets.Count == 0)
			await vm.CalendarVM.PrepareDataAsync();
		else
			await vm.CalendarVM.RefreshEntriesAsync();

		await vm.JournalVM.ReloadAsync(vm.CalendarVM.CurrentSelectedDate);

		if (vm.JournalVM.ShowAllDone)
			await AnimatePawsAsync();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		vm.CalendarVM.PropertyChanged -= OnCalendarVmPropertyChanged;
		vm.CalendarVM.TrackingChanged -= OnTrackingChanged;
		vm.JournalVM.PropertyChanged -= OnJournalVmPropertyChanged;
		vm.JournalVM.RequestOpenSheet -= OnRequestOpenSheet;
		vm.GlucoseSheetVM.Saved -= OnSheetSaved;
		vm.MoodSheetVM.Saved -= OnSheetSaved;
		vm.WeightSheetVM.Saved -= OnSheetSaved;
		vm.AppetiteSheetVM.Saved -= OnSheetSaved;
	}

	/// <summary>Numeric tracker "done" on the keyboard → save via the leaf's command.
	/// Retained for the (unused-but-defined) Tracker Hub input templates.</summary>
	private void OnTrackingValueCompleted(object? sender, EventArgs e)
	{
		if (sender is BindableObject bo && bo.BindingContext is TrackerLeaf leaf)
			leaf.SaveCommand.Execute(null);
	}

	private void OnTrackingChanged(string message) => ShowToast(message);

	async void OnMainClicked(object? sender, EventArgs args) => await Shell.Current.GoToAsync("//TodayTab");
	async void OnPetsClicked(object? sender, EventArgs args) => await Shell.Current.GoToAsync("//PetsTab");

	/// <summary>Jump-to-today: snap the selection back to the current date.</summary>
	private void OnJumpTodayClicked(object? sender, EventArgs e)
	{
		vm.CalendarVM.CurrentSelectedDate = DateTime.Now.Date;
		ShowToast(LocalizationManager.Instance.GetString("Toast_BackToday"));
	}

	// ── "Still to do" chips ───────────────────────────────────────────────────
	/// <summary>A chip was tapped. Medication chips log in one tap (with undo); the
	/// others slide up their sheet; the trailing "+" opens the add-anything sheet.</summary>
	private async void OnChipTapped(object? sender, TappedEventArgs e)
	{
		if (sender is not View v || v.BindingContext is not JournalChip chip)
			return;

		switch (chip.Kind)
		{
			case JournalChipKind.Add:
				vm.JournalVM.OpenAddSheetCommand.Execute(null);
				break;
			case JournalChipKind.Medication:
				await LogDoseFlowAsync(chip, v);
				break;
			default:
				await OpenSheetForKindAsync(chip.Kind);
				break;
		}
	}

	// One-tap dose: log, pop bubbles from the chip, refresh (chip vanishes, entry
	// appears), then a 6-second toast whose Undo removes the dose and restores it.
	private async Task LogDoseFlowAsync(JournalChip chip, View anchor)
	{
		var result = await vm.JournalVM.LogDoseAsync(chip);
		_ = BurstBubblesAsync(anchor);
		await ReloadJournalAsync();
		ShowUndoToast(result);
	}

	private void OnRequestOpenSheet(JournalChipKind kind) => _ = OpenAfterAddSheetAsync(kind);

	// Let the "+" sheet finish sliding out before the chosen sheet slides in.
	private async Task OpenAfterAddSheetAsync(JournalChipKind kind)
	{
		await Task.Delay(ReducedMotion.IsEnabled ? 60 : 220);
		await OpenSheetForKindAsync(kind);
	}

	private async Task OpenSheetForKindAsync(JournalChipKind kind)
	{
		int petId = vm.CalendarVM.CurrentPetId;
		string name = vm.CalendarVM.ActivePetName;
		var date = vm.CalendarVM.CurrentSelectedDate;

		switch (kind)
		{
			case JournalChipKind.Glucose: await vm.GlucoseSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Mood: await vm.MoodSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Weight: await vm.WeightSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Appetite: await vm.AppetiteSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Seizure: ShowToast(LocalizationManager.Instance.GetString("Journal_SeizureComingSoon")); break;
		}
	}

	// A sheet saved something → bubble-pop, refresh, undo-toast.
	private async void OnSheetSaved(JournalSaveResult result)
	{
		_ = BurstBubblesAtAsync(new Point(Width / 2, Height * 0.62));
		await ReloadJournalAsync();
		ShowUndoToast(result);
	}

	private async Task ReloadJournalAsync()
	{
		await vm.JournalVM.ReloadAsync(vm.CalendarVM.CurrentSelectedDate);
		await vm.CalendarVM.RefreshEntriesAsync();
	}

	private async void OnCalendarVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is nameof(CalendarViewModel.CurrentSelectedDate) or nameof(CalendarViewModel.ActivePetName))
			await vm.JournalVM.ReloadAsync(vm.CalendarVM.CurrentSelectedDate);
	}

	private async void OnJournalVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(JournalLogViewModel.ShowAllDone) && vm.JournalVM.ShowAllDone)
			await AnimatePawsAsync();
	}

	// ── Undo ───────────────────────────────────────────────────────────────────
	private async void OnUndoTapped(object? sender, TappedEventArgs e)
	{
		var undo = _pendingUndo;
		_pendingUndo = null;
		_toastSeq++;              // cancel the pending auto-hide
		HideToast();

		if (undo != null)
			await undo();
		await ReloadJournalAsync();
	}

	// ── Toast ──────────────────────────────────────────────────────────────────
	private void ShowToast(string message) => ShowToastCore(message, null);

	private void ShowUndoToast(JournalSaveResult result) => ShowToastCore(result.Message, result.UndoAsync);

	private async void ShowToastCore(string message, Func<Task>? undo)
	{
		ToastLabel.Text = message;
		_pendingUndo = undo;
		UndoButton.IsVisible = undo != null;
		// Only capture taps while an Undo button is present; otherwise stay
		// input-transparent so the transient toast never blocks the Journal.
		Toast.InputTransparent = undo == null;

		int seq = ++_toastSeq;
		int ms = undo != null ? 6000 : 2400; // undo stays 6s so it's reachable

		if (ReducedMotion.IsEnabled)
		{
			Toast.TranslationY = 0;
			Toast.Opacity = 1;
			await Task.Delay(ms);
			if (seq == _toastSeq)
				HideToast();
			return;
		}

		Toast.TranslationY = 16;
		Toast.Opacity = 0;
		await Task.WhenAll(
			Toast.FadeTo(1, 220, Easing.CubicOut),
			Toast.TranslateTo(0, 0, 220, Easing.CubicOut));

		await Task.Delay(ms);
		if (seq != _toastSeq)
			return;
		await Toast.FadeTo(0, 260, Easing.CubicIn);
		HideToast();
	}

	private void HideToast()
	{
		Toast.Opacity = 0;
		Toast.InputTransparent = true;
		UndoButton.IsVisible = false;
		_pendingUndo = null;
	}

	// ── Reusable confirm burst: bubbles rise + fade from an anchor / point ──
	private Task BurstBubblesAsync(View anchor)
	{
		if (anchor.Width <= 0)
			return Task.CompletedTask;
		var origin = GetPositionInPage(anchor);
		return BurstBubblesAtAsync(new Point(origin.X + anchor.Width / 2, origin.Y + anchor.Height / 2));
	}

	private async Task BurstBubblesAtAsync(Point center)
	{
		if (ReducedMotion.IsEnabled)
			return;

		double cx = center.X, cy = center.Y;
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
			bubble.ScaleTo(1.0, durationMs, Easing.CubicOut),
			bubble.FadeTo(0, durationMs, Easing.CubicIn));
		EffectLayer.Remove(bubble);
	}

	/// <summary>Position of an element relative to the page, walking up the visual
	/// tree and discounting the scroll offset.</summary>
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

	/// <summary>Fade the four paw glyphs in with a staggered delay (skipped when the
	/// OS asks for reduced motion — they simply appear at full opacity).</summary>
	private async Task AnimatePawsAsync()
	{
		var paws = new[] { Paw1, Paw2, Paw3, Paw4 };

		if (ReducedMotion.IsEnabled)
		{
			foreach (var paw in paws)
				paw.Opacity = 0.85;
			return;
		}

		foreach (var paw in paws)
			paw.Opacity = 0;

		for (int i = 0; i < paws.Length; i++)
		{
			await Task.Delay(180);
			_ = paws[i].FadeTo(0.85, 500, Easing.CubicOut);
		}
	}
}
