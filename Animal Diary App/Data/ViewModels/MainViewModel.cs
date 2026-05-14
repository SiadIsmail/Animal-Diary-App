using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;

namespace Animal_Diary_App.Data.ViewModels;

public class MainViewModel
{
    public CalendarViewModel CalendarVM { get; }
    public MainPageViewModel MainPageVM { get; }
    public PetViewModel PetVM { get; }

    public MainViewModel(PetService petService, PetEntryService petEntryService)
    {
        

        CalendarVM = new CalendarViewModel(petEntryService);
        MainPageVM = new MainPageViewModel(petEntryService);
        PetVM = new PetViewModel(petService);
    }
    public async Task LoadAsync()
    {
        await PetVM.LoadPetsAsync();
        await MainPageVM.LoadLatestWeightAsync();
    }
}