namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Data.Services.Journal;
using Animal_Diary_App.Helpers;
using Microsoft.Maui.Controls.Shapes;

// This file's own namespace is ...Data.View, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

public partial class CalendarPage : ContentPage
{
	private MainViewModel vm;

	// The (pet, date) context of the last-started Journal reload. CalendarVM's
	// NotifyDerived raises ActivePetName on EVERY entries/doses load, so without
	// this marker each save/refresh cascaded into 2–4 redundant full-day gathers.
	private int _lastJournalPetId = -1;
	private DateTime _lastJournalDate = DateTime.MinValue;

	// The kind of the sheet most recently opened, so the single OnSheetSaved funnel
	// (which is shared by all five sheets and doesn't otherwise know the kind) can tag
	// journal_entry_created with the right entry_type. Undo does not pass through here.
	private JournalChipKind? _lastOpenedSheetKind;

	public CalendarPage(MainViewModel mainViewModel)
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

		// Engagement signal: the Journal tab was opened.
		vm.Analytics.Track(AnalyticsEvents.CalendarOpened);

		vm.CalendarVM.PropertyChanged += OnCalendarVmPropertyChanged;
		vm.JournalVM.PropertyChanged += OnJournalVmPropertyChanged;
		vm.JournalVM.RequestOpenSheet += OnRequestOpenSheet;
		vm.JournalVM.ItemDeleted += OnItemDeleted;
		vm.GlucoseSheetVM.Saved += OnSheetSaved;
		vm.MoodSheetVM.Saved += OnSheetSaved;
		vm.WeightSheetVM.Saved += OnSheetSaved;
		vm.AppetiteSheetVM.Saved += OnSheetSaved;
		vm.SeizureSheetVM.Saved += OnSheetSaved;
		vm.WaterSheetVM.Saved += OnSheetSaved;

		// Another caregiver's changes landing while the Journal is visible reload
		// it in place — same stale-context path an appearance uses.
		vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;

		await ReloadDataAsync();
	}

	/// <summary>The Journal's full stale-context reload — runs on every appearance
	/// AND when a cloud sync applies remote changes while the page is visible.
	/// Routes through the (pet, date) marker so it stays deduped.</summary>
	private async Task ReloadDataAsync()
	{
		try
		{
			// Data may have changed on other tabs while we were away — mark the
			// Journal context stale so exactly one reload runs for this appearance
			// (usually via the property-changed handler as PrepareDataAsync loads).
			_lastJournalPetId = -1;
			_lastJournalDate = DateTime.MinValue;

			// Reload the pet list too (one small query): a pet added on the Pets
			// tab must appear in the Journal's chips without passing through Today.
			await vm.CalendarVM.PrepareDataAsync();

			// If nothing the handler listens to fired, load explicitly.
			if (_lastJournalPetId == -1)
			{
				_lastJournalPetId = vm.CalendarVM.CurrentPetId;
				_lastJournalDate = vm.CalendarVM.CurrentSelectedDate;
				await vm.JournalVM.ReloadAsync(_lastJournalDate);
			}

			if (vm.JournalVM.ShowAllDone)
				await AnimatePawsAsync();
		}
		catch (Exception ex)
		{
			// A failed load must degrade to an empty page, never crash the app
			// (async void callers — an escaping exception here kills the process).
			System.Diagnostics.Debug.WriteLine($"[CalendarPage] reload failed: {ex}");
		}
	}

	private void OnRemoteChangesApplied() =>
		MainThread.BeginInvokeOnMainThread(async () => await ReloadDataAsync());

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
		vm.CalendarVM.PropertyChanged -= OnCalendarVmPropertyChanged;
		vm.JournalVM.PropertyChanged -= OnJournalVmPropertyChanged;
		vm.JournalVM.RequestOpenSheet -= OnRequestOpenSheet;
		vm.JournalVM.ItemDeleted -= OnItemDeleted;
		vm.GlucoseSheetVM.Saved -= OnSheetSaved;
		vm.MoodSheetVM.Saved -= OnSheetSaved;
		vm.WeightSheetVM.Saved -= OnSheetSaved;
		vm.AppetiteSheetVM.Saved -= OnSheetSaved;
		vm.SeizureSheetVM.Saved -= OnSheetSaved;
		vm.WaterSheetVM.Saved -= OnSheetSaved;
	}

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

		try
		{
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
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[CalendarPage] chip tap failed: {ex}");
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

	// Emit journal_entry_created for the sheet that just saved. Only the coarse entry
	// kind is sent — never the logged value, note, or the pet.
	private void TrackJournalEntry()
	{
		var entryType = _lastOpenedSheetKind switch
		{
			JournalChipKind.Mood => AnalyticsEvents.EntryTypeMood,
			JournalChipKind.Weight => AnalyticsEvents.EntryTypeWeight,
			JournalChipKind.Glucose => AnalyticsEvents.EntryTypeGlucose,
			JournalChipKind.Appetite => AnalyticsEvents.EntryTypeAppetite,
			JournalChipKind.Seizure => AnalyticsEvents.EntryTypeSeizure,
			JournalChipKind.Water => AnalyticsEvents.EntryTypeWater,
			_ => null,
		};
		if (entryType is null)
			return;

		vm.Analytics.Track(AnalyticsEvents.JournalEntryCreated, new Dictionary<string, object?>
		{
			[AnalyticsEvents.PropEntryType] = entryType,
		});
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

		// Remember which sheet we opened so OnSheetSaved can tag the entry_type.
		_lastOpenedSheetKind = kind;

		switch (kind)
		{
			case JournalChipKind.Glucose: await vm.GlucoseSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Mood: await vm.MoodSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Weight: await vm.WeightSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Appetite: await vm.AppetiteSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Seizure: await vm.SeizureSheetVM.OpenAsync(petId, name, date); break;
			case JournalChipKind.Water: await vm.WaterSheetVM.OpenAsync(petId, name, date); break;
		}
	}

	// A sheet saved something → bubble-pop, refresh, undo-toast.
	private async void OnSheetSaved(JournalSaveResult result)
	{
		try
		{
			// "Which logging features are actually used?" — one event per sheet save,
			// tagged with the kind. This funnel is the forward save path only; undo
			// runs through the toast callback, so undone saves aren't counted.
			TrackJournalEntry();

			_ = BurstBubblesAtAsync(new Point(Width / 2, Height * 0.62));
			await ReloadJournalAsync();
			ShowUndoToast(result);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[CalendarPage] OnSheetSaved failed: {ex}");
		}
	}

	// A timeline entry was deleted → refresh, then a 6-second undo-toast that restores
	// it. No bubble-pop: a deletion isn't a "logged something" celebration.
	private async void OnItemDeleted(JournalSaveResult result)
	{
		try
		{
			await ReloadJournalAsync();
			ShowUndoToast(result);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[CalendarPage] OnItemDeleted failed: {ex}");
		}
	}

	// Explicit full refresh after a mutation (save / delete / undo). Always runs;
	// stamps the context marker so RefreshEntriesAsync's derived-property
	// notifications don't bounce back through the handler below.
	private async Task ReloadJournalAsync()
	{
		_lastJournalPetId = vm.CalendarVM.CurrentPetId;
		_lastJournalDate = vm.CalendarVM.CurrentSelectedDate;
		await vm.JournalVM.ReloadAsync(_lastJournalDate);
		await vm.CalendarVM.RefreshEntriesAsync();
	}

	private async void OnCalendarVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName is not (nameof(CalendarViewModel.CurrentSelectedDate) or nameof(CalendarViewModel.ActivePetName)))
			return;

		// ActivePetName is raised by every NotifyDerived, not just real pet
		// switches — only reload when the (pet, date) context actually changed.
		var petId = vm.CalendarVM.CurrentPetId;
		var date = vm.CalendarVM.CurrentSelectedDate;
		if (petId == _lastJournalPetId && date == _lastJournalDate)
			return;

		_lastJournalPetId = petId;
		_lastJournalDate = date;
		try
		{
			await vm.JournalVM.ReloadAsync(date);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"[CalendarPage] journal reload failed: {ex}");
		}
	}

	private async void OnJournalVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(JournalLogViewModel.ShowAllDone) && vm.JournalVM.ShowAllDone)
			await AnimatePawsAsync();
	}

	// ── Toast (the shared UndoToast control; undo reloads the Journal too) ─────
	private void ShowToast(string message) => Toast.Show(message);

	private void ShowUndoToast(JournalSaveResult result) =>
		Toast.Show(result.Message, async () =>
		{
			await result.UndoAsync();
			await ReloadJournalAsync();
		});

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
