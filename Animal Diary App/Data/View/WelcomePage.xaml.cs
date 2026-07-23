namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services.Analytics;
using Animal_Diary_App.Helpers;
using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;

public partial class WelcomePage : ContentPage
{

	private MainViewModel vm;

	// WelcomePage is only ever shown as the first screen of first-launch onboarding,
	// so its appearance IS the start of onboarding. Guarded so navigating back to it
	// (Welcome → create pet → back) doesn't re-fire the funnel's entry event.
	private bool _onboardingStartTracked;

	// A returning user who signs in — or a caregiver who redeems an invite code — pulls
	// their pet(s) down through the account sheet hosted here. That first sync raises
	// RemoteChangesApplied; once pets exist we hand straight off to the tabbed app
	// instead of making them create a pet. Guarded so a second sync can't hand off twice.
	private bool _handedOff;

	public WelcomePage(MainViewModel mainViewModel)
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

		if (!_onboardingStartTracked)
		{
			_onboardingStartTracked = true;
			vm.Analytics.Track(AnalyticsEvents.OnboardingStarted);
		}

		// The account sheet's Delete-account action needs a native confirm, same as its
		// other two hosts (MainPage / PetsPage); without it the command would delete
		// with no confirmation.
		vm.CloudVM.ConfirmDeleteAccount = () =>
			DisplayAlert(
				LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmTitle"),
				LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmMessage"),
				LocalizationManager.Instance.GetString("Cloud_DeleteAccountConfirmAccept"),
				LocalizationManager.Instance.GetString("Common_Cancel"));

		// Signing in or joining a shared pet pulls data down; the first sync that adds a
		// pet lands here and completes onboarding straight into the app.
		vm.CloudSync.RemoteChangesApplied += OnRemoteChangesApplied;
	}

	protected override void OnDisappearing()
	{
		base.OnDisappearing();
		vm.CloudVM.ConfirmDeleteAccount = null;
		vm.CloudSync.RemoteChangesApplied -= OnRemoteChangesApplied;
	}

	async void OnAddPetClicked(object? sender, EventArgs args)
	{
		await Navigation.PushAsync(new CreatePetPage(vm));
	}

	// The one account door. The shared sheet decides what to show: signed out → create
	// account / sign in / Google; signed in → backup + "join a shared pet" (the code
	// path a caregiver needs, which is why an account has to come first).
	void OnSignInClicked(object? sender, EventArgs args)
	{
		vm.CloudVM.OpenCommand.Execute(null);
	}

	private void OnRemoteChangesApplied() =>
		MainThread.BeginInvokeOnMainThread(async () =>
		{
			if (_handedOff)
				return;
			try
			{
				// Load whatever synced down and pick the active pet (saved-or-first),
				// exactly as startup does. No pets means nothing to restore yet — a
				// brand-new account still creates its first pet through the primary CTA.
				await vm.PetVM.LoadPetsAsync();
				if (_handedOff || vm.PetVM.Pets.Count == 0)
					return;

				_handedOff = true;

				// Close the sheet so it doesn't reappear on the next host (the VM is a
				// singleton shared with MainPage / PetsPage), then hand off exactly as
				// the condition picker does at the end of the create-a-pet flow.
				vm.CloudVM.DismissCommand.Execute(null);
				vm.Analytics.Track(AnalyticsEvents.OnboardingCompleted);
				(Application.Current as App)?.SwitchToMainApp();
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"[Welcome] restore handoff failed: {ex}");
			}
		});
}
