namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using System.ComponentModel;
using System.Runtime.CompilerServices;

public class PetViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    private string enteredPetName = string.Empty;

    public string EnteredPetName
    {
        get => enteredPetName;
        set
        {
            if (enteredPetName == value)
            {
                return;
            }

            enteredPetName = value;
            OnPropertyChanged();
        }
    }

    private string enteredPetType = string.Empty;

    public string EnteredPetType
    {
        get => enteredPetType;
        set
        {
            if (enteredPetType == value)
            {
                return;
            }

            enteredPetType = value;
            OnPropertyChanged();
        }
    }

    private string enteredPetAge = string.Empty;

    public string EnteredPetAge
    {
        get => enteredPetAge;
        set
        {
            if (enteredPetAge == value)
            {
                return;
            }

            enteredPetAge = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ParsedPetAge));
        }
    }

    private readonly PetDatabase _database;

    public PetViewModel(PetDatabase database)
    {
        _database = database;
    }
    public async Task SavePetAsync()
    {
        if (ParsedPetAge == null)
            return;

        var pet = new Pet
        {
            Name = EnteredPetName,
            Type = EnteredPetType,
            Age = ParsedPetAge.Value
        };
        await _database.SavePetAsync(pet);
    }
    List<Pet> pets = new List<Pet>();

    public List<Pet> Pets
    {
        get => pets;
        set
        {
            pets = value;
            OnPropertyChanged();
        }
    }

    public async Task LoadPetsAsync()
    {
        var allPets = await _database.GetPetsAsync();

        var latestPet = allPets
            .OrderByDescending(p => p.Id)
            .FirstOrDefault();

        Pets = latestPet != null
            ? new List<Pet> { latestPet }
            : new List<Pet>();
    }

    public int? ParsedPetAge =>
        int.TryParse(EnteredPetAge, out var age) ? age : null;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}