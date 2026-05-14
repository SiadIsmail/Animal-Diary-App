namespace Animal_Diary_App.Data.Services;
using Animal_Diary_App.Data.Models;
using SQLite;

public class MedicationService
{
    private readonly SQLiteAsyncConnection _db;

    public MedicationService(AppDatabase database)
    {
        _db = database.Connection;
    }
}