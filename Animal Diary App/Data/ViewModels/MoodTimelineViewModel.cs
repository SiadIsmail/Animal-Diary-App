namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.Services;
using Animal_Diary_App.Helpers;
using System.Collections.ObjectModel;

public class MoodTimelineDay
{
    public DateTime Date { get; set; }
    public MoodLevel Mood { get; set; }
    public Color MoodColor => Mood.GetColor();
    public string DateShort => Date.ToString("M/d");
}

public class MoodTimelineViewModel
{
    private readonly PetEntryService _petEntryService;

    public ObservableCollection<MoodTimelineDay> TimelineDays { get; } = new();

    private bool hasMoodData;
    public bool HasMoodData
    {
        get => hasMoodData;
        set => hasMoodData = value;
    }

    public MoodTimelineViewModel(PetEntryService petEntryService)
    {
        _petEntryService = petEntryService;
    }

    public async Task LoadLast30DaysAsync(int petId)
    {
        TimelineDays.Clear();

        var endDate = DateTime.Now.Date;
        var startDate = endDate.AddDays(-29);

        var entries = await _petEntryService.GetLast30DaysMoodEntriesAsync(petId);
        var entryDict = entries.ToDictionary(e => e.Date.Date);

        for (int i = 0; i < 30; i++)
        {
            var date = startDate.AddDays(i);
            var mood = entryDict.ContainsKey(date) ? (MoodLevel)entryDict[date].MoodLevel : MoodLevel.None;
            TimelineDays.Add(new MoodTimelineDay { Date = date, Mood = mood });
        }

        HasMoodData = entries.Count > 0;
    }
}
