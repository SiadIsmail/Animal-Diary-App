namespace Animal_Diary_App.Data.Services.Cloud;

/// <summary>Registered when <see cref="CloudConfig.Enabled"/> is false — the app
/// carries zero cloud behaviour, mirroring <c>NullAnalyticsService</c>.</summary>
public sealed class NullCloudSyncService : ICloudSyncService
{
    public bool IsBackupEnabled => false;
    public DateTime? LastSyncedUtc => null;
    public event Action? StateChanged { add { } remove { } }

    public Task InitializeAsync() => Task.CompletedTask;
    public string? GetPetRole(string petSyncId) => null;
    public Task<SyncOutcome> SyncNowAsync() => Task.FromResult(SyncOutcome.BackupDisabled);
    public void RequestSyncSoon() { }
    public Task<SyncOutcome> EnableBackupAsync() => Task.FromResult(SyncOutcome.BackupDisabled);
    public Task DisableBackupAsync() => Task.CompletedTask;
    public Task DeleteCloudDataAsync() => Task.CompletedTask;
    public Task DeleteAccountAsync() => Task.CompletedTask;
}
