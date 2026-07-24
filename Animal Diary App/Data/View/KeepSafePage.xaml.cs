namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Helpers;

/// <summary>
/// The post-first-pet backup/share offer (see the XAML). Reached only from the end of
/// first-launch onboarding, and only when backup isn't already on. Reuses the shared
/// account sheet; turning backup on (CloudSync.StateChanged → IsBackupEnabled) or
/// tapping "Maybe later" completes onboarding into the app. Gentle and skippable.
/// </summary>
public partial class KeepSafePage : ContentPage
{
	private readonly MainViewModel vm;

	// Both exits (backup turned on, or "Maybe later") end onboarding — guard so they
	// can't both fire and hand off twice.
	private bool _handedOff;

	public KeepSafePage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	// Android back closes the open account sheet before it navigates.
	protected override bool OnBackButtonPressed()
		=> Controls.BackDismiss.TryCloseTopmostOverlay(this) || base.OnBackButtonPressed();

	protected override void OnAppearing()
	{
		base.OnAppearing();

		TitleLabel.Text = LocalizationManager.Instance.Format(
			"KeepSafe_Title", vm.PetVM.ActivePet?.Name ?? string.Empty);

		// The account sheet's Delete-account action needs a native confirm, same as its
		// other hosts (MainPage / PetsPage).
		vm.CloudVM.ConfirmDeleteAccount = () =>
			DisplayAlert(
				LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmTitle"),
				LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmMessage"),
				LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmAccept"),
				LocalizationManager.Instance.GetString("Common_Cancel"));

		// Turning backup on inside the sheet fulfils the offer — hand off to the app.
		vm.CloudSync.StateChanged += OnCloudStateChanged;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		vm.CloudVM.ConfirmDeleteAccount = null;
		vm.CloudSync.StateChanged -= OnCloudStateChanged;
	}

	void OnSetUpClicked(object? sender, EventArgs args)
	{
		vm.CloudVM.OpenCommand.Execute(null);
	}

	void OnLaterClicked(object? sender, EventArgs args)
	{
		HandOff();
	}

	private void OnCloudStateChanged() =>
		MainThread.BeginInvokeOnMainThread(() =>
		{
			// Only the backup-on transition matters; other state changes (a sync tick
			// finishing) leave the offer standing.
			if (vm.CloudSync.IsBackupEnabled)
				HandOff();
		});

	private void HandOff()
	{
		if (_handedOff)
			return;
		_handedOff = true;

		// Close the sheet so it doesn't reappear on the next host (the VM is a singleton
		// shared with MainPage / PetsPage), then finish onboarding into the app — the
		// same handoff the condition picker and the Welcome restore path use.
		vm.CloudVM.DismissCommand.Execute(null);
		vm.Analytics.Track(AnalyticsEvents.OnboardingCompleted);
		(Application.Current as App)?.SwitchToMainApp();
	}
}
