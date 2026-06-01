namespace Animal_Diary_App.Data.Services;

using Animal_Diary_App.Data.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class ActivePetService : INotifyPropertyChanged
{
    private Pet activePet = new Pet();
    private readonly AppDatabase _db;
    private const string ActivePetIdKey = "ActivePetId";

    public Pet ActivePet
    {
        get => activePet;
        set
        {
            if (!Equals(activePet, value))
            {
                activePet = value;
                OnPropertyChanged();
                _ = SaveActivePetIdAsync();
            }
        }
    }

    public ActivePetService(AppDatabase db)
    {
        _db = db;
    }

    public async Task LoadActivePetAsync(int petId)
    {
        var pet = await _db.Connection.FindAsync<Pet>(petId);
        if (pet != null)
        {
            activePet = pet;
            OnPropertyChanged(nameof(ActivePet));
        }
    }

    private async Task SaveActivePetIdAsync()
    {
        try
        {
            var setting = await _db.Connection.FindAsync<AppSettings>(ActivePetIdKey);
            if (setting == null)
            {
                setting = new AppSettings { Key = ActivePetIdKey, Value = activePet.Id.ToString() };
                await _db.Connection.InsertAsync(setting);
            }
            else
            {
                setting.Value = activePet.Id.ToString();
                await _db.Connection.UpdateAsync(setting);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error saving active pet: {ex.Message}");
        }
    }

    public async Task<int> GetSavedActivePetIdAsync()
    {
        try
        {
            var setting = await _db.Connection.FindAsync<AppSettings>(ActivePetIdKey);
            if (setting != null && int.TryParse(setting.Value, out var petId))
            {
                Console.WriteLine($"Loaded active pet ID: {petId}");
                return petId;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error loading active pet ID: {ex.Message}");
        }
        return 0;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
