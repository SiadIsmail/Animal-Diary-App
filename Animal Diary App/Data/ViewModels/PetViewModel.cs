namespace Animal_Diary_App.Data.ViewModels;

using System.ComponentModel;
using System.Runtime.CompilerServices;

public class PetViewModel : INotifyPropertyChanged
{
    private string enteredPetName = string.Empty;
    private string enteredPetType = string.Empty;
    private string enteredPetAge = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;

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

    public int? ParsedPetAge =>
        int.TryParse(EnteredPetAge, out var age) ? age : null;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}