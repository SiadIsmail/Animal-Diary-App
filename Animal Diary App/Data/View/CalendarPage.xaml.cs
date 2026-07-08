namespace Animal_Diary_App.Data.View;

using System.ComponentModel;
using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Controls.Shapes;

// This file's own namespace is ...Data.View, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

public partial class CalendarPage : ContentPage
{
	private MainViewModel vm;
	private int _toastSeq;

	public CalendarPage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}


	protected override async void OnAppearing()
	{
		base.OnAppearing();

		// Play the paw celebration whenever the day's care becomes fully complete.
		// Subscribe here (and unsubscribe in OnDisappearing) so handlers don't
		// accumulate across navigations onto the same shared CalendarVM.
		vm.CalendarVM.PropertyChanged += OnCalendarVmPropertyChanged;

		if (vm.CalendarVM.Pets.Count == 0)
			await vm.CalendarVM.PrepareDataAsync();
		else
			await vm.CalendarVM.RefreshEntriesAsync();

		if (vm.CalendarVM.AllCareComplete)
			await AnimatePawsAsync();
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		vm.CalendarVM.PropertyChanged -= OnCalendarVmPropertyChanged;
	}

	async void OnMainClicked(object? sender, EventArgs args)
	{
		await Shell.Current.GoToAsync("//TodayTab");
	}
	async void OnPetsClicked(object? sender, EventArgs args)
	{
		await Shell.Current.GoToAsync("//PetsTab");
	}

	// ── Confirm actions: run the command, then celebrate (toast + burst) ──

	private void OnMoodConfirmClicked(object? sender, EventArgs e)
	{
		vm.CalendarVM.OnMoodEntryCompleted.Execute(null);
		ShowToast(MoodSavedToast());
		if (sender is View anchor)
			_ = BurstBubblesAsync(anchor);
	}

	private async void OnWeightEntryCompleted(object? sender, EventArgs e)
	{
		vm.CalendarVM.OnWeightEntryCompleted.Execute(null);
		ShowToast(WeightSavedToast());
		if (sender is View anchor)
			await BurstBubblesAsync(anchor);
	}

	private void OnMarkGivenClicked(object? sender, EventArgs e)
	{
		if (sender is not View anchor || anchor.BindingContext is not DoseItem dose)
			return;

		vm.CalendarVM.ToggleDoseTakenCommand.Execute(dose);
		ShowToast(MedGivenToast());
		_ = BurstBubblesAsync(anchor);
	}

	/// <summary>Jump-to-today: snap the selection back to the current date.</summary>
	private void OnJumpTodayClicked(object? sender, EventArgs e)
	{
		vm.CalendarVM.CurrentSelectedDate = DateTime.Now.Date;
		ShowToast(LocalizationManager.Instance.GetString("Toast_BackToday"));
	}

	/// <summary>Third quick-log button: bring the day's timeline (doses) into view.</summary>
	private async void OnMedsQuickTapped(object? sender, EventArgs e)
	{
		try
		{
			await JournalScroll.ScrollToAsync(TimelineSection, ScrollToPosition.Start, animated: true);
		}
		catch
		{
			// Scrolling is a nicety — never let a layout race throw into the UI.
		}
	}

	private async void OnCalendarVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
	{
		if (e.PropertyName == nameof(CalendarViewModel.AllCareComplete) && vm.CalendarVM.AllCareComplete)
			await AnimatePawsAsync();
	}

	// ── Rotating warm copy (pulled from the HTML COPY object) ─────────────
	private string MedGivenToast() => PickCopy(new[] { "Toast_MedGiven1", "Toast_MedGiven2", "Toast_MedGiven3" });
	private string MoodSavedToast() => PickCopy(new[] { "Toast_MoodSaved1", "Toast_MoodSaved2" });
	private string WeightSavedToast() => PickCopy(new[] { "Toast_WeightSaved1", "Toast_WeightSaved2" });

	/// <summary>Pick one variant at random and format it with the active pet's
	/// name ({0}); variants without a placeholder simply ignore the argument.</summary>
	private string PickCopy(string[] keys)
	{
		var key = keys[Random.Shared.Next(keys.Length)];
		return LocalizationManager.Instance.Format(key, vm.CalendarVM.ActivePetName);
	}

	// ── Toast overlay ─────────────────────────────────────────────────────
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

	// ── Reusable confirm burst: small bubbles rise + fade from an anchor ──
	/// <summary>Emit a short burst of bubbles that rise and dissipate from the
	/// centre of <paramref name="anchor"/>. Reusable from any "confirm" action;
	/// silently does nothing when reduced motion is requested.</summary>
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
			bubble.ScaleTo(1.0, durationMs, Easing.CubicOut),
			bubble.FadeTo(0, durationMs, Easing.CubicIn));
		EffectLayer.Remove(bubble);
	}

	/// <summary>Position of an element relative to the page/effect layer, walking
	/// up the visual tree and discounting the scroll offset.</summary>
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

	/// <summary>Fade the four paw glyphs in with a staggered delay (skipped when
	/// the OS asks for reduced motion — they simply appear at full opacity).</summary>
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
