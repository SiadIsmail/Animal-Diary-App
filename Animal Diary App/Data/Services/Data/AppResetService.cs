namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;

public class AppResetService
{
    private readonly AppDatabase _db;
    private readonly ActivePetService _activePetService;

    public AppResetService(AppDatabase db, ActivePetService activePetService)
    {
        _db = db;
        _activePetService = activePetService;
    }

    public async Task ResetDataAsync()
    {
        await _db.Connection.DeleteAllAsync<Pet>();
        await _db.Connection.DeleteAllAsync<PetEntry>();
        await _db.Connection.DeleteAllAsync<Medication>();
        await _db.Connection.DeleteAllAsync<AppSettings>();
        await _db.Connection.DeleteAllAsync<MedicationSchedule>();
        await _db.Connection.DeleteAllAsync<MedicationTime>();

        MainThread.BeginInvokeOnMainThread(() =>
        {
            _activePetService.ActivePet = new Pet();
        });
    }
}
