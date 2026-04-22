namespace Animal_Diary_App.Data.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public class MainPageViewModel : INotifyPropertyChanged
{
    private readonly PetViewModel petViewModel;
    private readonly CalendarViewModel calendarViewModel;

    public event PropertyChangedEventHandler? PropertyChanged;

    public MainPageViewModel(PetViewModel petViewModel, CalendarViewModel calendarViewModel)
    {
        this.petViewModel = petViewModel;
        this.calendarViewModel = calendarViewModel;

        this.petViewModel.PropertyChanged += OnPetViewModelPropertyChanged;
        this.calendarViewModel.PropertyChanged += OnCalendarViewModelPropertyChanged;
    }

    public string PetName => petViewModel.EnteredPetName;

    public string PetDetails
    {
        get
        {
            var ageText = petViewModel.ParsedPetAge?.ToString() ?? "unknown";
            return $"{petViewModel.EnteredPetType}, {ageText} years old{Environment.NewLine}Your pet is {calendarViewModel.EnteredMood} right now.";
        }
    }

    private void OnPetViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(PetViewModel.EnteredPetName))
        {
            OnPropertyChanged(nameof(PetName));
            return;
        }

        if (e.PropertyName is nameof(PetViewModel.EnteredPetType) or nameof(PetViewModel.EnteredPetAge) or nameof(PetViewModel.ParsedPetAge))
        {
            OnPropertyChanged(nameof(PetDetails));
        }
    }

    private void OnCalendarViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(CalendarViewModel.EnteredMood))
        {
            OnPropertyChanged(nameof(PetDetails));
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
