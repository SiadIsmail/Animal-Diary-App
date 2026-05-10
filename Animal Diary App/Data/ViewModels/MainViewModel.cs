using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;

namespace Animal_Diary_App.Data.ViewModels;

public class MainViewModel
{
    public CalendarViewModel CalendarVM { get; }
    public MainPageViewModel MainPageVM { get; }
    public PetViewModel PetVM { get; }

    private readonly PetDatabase petdatabase;
    private readonly PetEntryDatabase petentrydatabase;


    public MainViewModel()
    {
        petdatabase = new PetDatabase();
        petentrydatabase = new PetEntryDatabase();


        CalendarVM = new CalendarViewModel(petentrydatabase);
        MainPageVM = new MainPageViewModel(petentrydatabase);
        PetVM = new PetViewModel(petdatabase);
    }
    public async Task LoadAsync()
    {
        await PetVM.LoadPetsAsync();
        await MainPageVM.LoadLatestWeightAsync();
    }
}