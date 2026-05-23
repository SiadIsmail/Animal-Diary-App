using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;

namespace Animal_Diary_App.Data.ViewModels;

public class MainViewModel
{
    public CalendarViewModel CalendarVM { get; }
    public MainPageViewModel MainPageVM { get; }
    public PetViewModel PetVM { get; }
    public MedicationViewModel MedicationVM { get; }
    

    public MainViewModel(
 CalendarViewModel calendarVM,
 MainPageViewModel mainPageVM,
 PetViewModel petVM,
 MedicationViewModel medicationVM)
    {
        MainPageVM = mainPageVM;
        PetVM = petVM;
        MedicationVM = medicationVM;
        CalendarVM = calendarVM;
    }
    public async Task LoadAsync()
    {
        await PetVM.LoadPetsAsync();
        await Task.WhenAll(
            MainPageVM.LoadCurrentPet(),
            MainPageVM.LoadLatestWeightAsync(),
            CalendarVM.PrepareDataAsync()
            );
    }
}