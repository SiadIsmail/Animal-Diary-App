namespace Animal_Diary_App.Data.ViewModels;

using Animal_Diary_App.Data.Services;
using System.Windows.Input;

public class SettingsViewModel : BaseViewModel
{
    private bool _isPanelOpen;
    public bool IsPanelOpen
    {
        get => _isPanelOpen;
        set => SetProperty(ref _isPanelOpen, value);
    }

    public event EventHandler? ResetCompleted;

    /// <summary>
    /// Set by the active page to show a native confirmation dialog before deletion.
    /// Returns true if the user confirmed, false to cancel.
    /// </summary>
    public Func<Task<bool>>? ConfirmDeleteAllData { get; set; }

    public ICommand OpenSettingsCommand { get; }
    public ICommand CloseSettingsCommand { get; }
    public ICommand DeleteAllDataCommand { get; }

    private readonly AppResetService _appResetService;

    public SettingsViewModel(AppResetService appResetService)
    {
        _appResetService = appResetService;
        OpenSettingsCommand = new Command(() => IsPanelOpen = true);
        CloseSettingsCommand = new Command(() => IsPanelOpen = false);
        DeleteAllDataCommand = new Command(async () => await OnDeleteAllDataAsync());
    }

    private async Task OnDeleteAllDataAsync()
    {
        if (ConfirmDeleteAllData != null)
        {
            var confirmed = await ConfirmDeleteAllData();
            if (!confirmed)
                return;
        }

        await _appResetService.ResetDataAsync();
        IsPanelOpen = false;
        ResetCompleted?.Invoke(this, EventArgs.Empty);
    }
}
