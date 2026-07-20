namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;
using Animal_Diary_App.Data.Services.Analytics;
using Microsoft.Extensions.DependencyInjection;
using Animal_Diary_App.Data.Services;

public partial class WelcomePage : ContentPage
{

	private MainViewModel vm;

	// WelcomePage is only ever shown as the first screen of first-launch onboarding,
	// so its appearance IS the start of onboarding. Guarded so navigating back to it
	// (Welcome → create pet → back) doesn't re-fire the funnel's entry event.
	private bool _onboardingStartTracked;

	public WelcomePage(MainViewModel mainViewModel)
	{
		InitializeComponent();
		vm = mainViewModel;
		BindingContext = vm;
	}

	protected override void OnAppearing()
	{
		base.OnAppearing();

		if (!_onboardingStartTracked)
		{
			_onboardingStartTracked = true;
			vm.Analytics.Track(AnalyticsEvents.OnboardingStarted);
		}
	}

	async void OnAddPetClicked(object? sender, EventArgs args)
	{
		await Navigation.PushAsync(new CreatePetPage(vm));
	}
}
