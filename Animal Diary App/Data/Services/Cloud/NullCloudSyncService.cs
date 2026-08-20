namespace Animal_Diary_App.Data.Services.Cloud;

/// <summary>Registered when <see cref="CloudConfig.Enabled"/> is false — the app
/// carries zero cloud behaviour, mirroring <c>NullAnalyticsService</c>.</summary>
public sealed class NullCloudSyncService : ICloudSyncService, Billing.IPetAccessSource
{
    public bool IsBackupEnabled => false;
    public DateTime? LastSyncedUtc => null;
    public event Action? StateChanged { add { } remove { } }
    public event Action? RemoteChangesApplied { add { } remove { } }
    public event Action<IReadOnlyList<string>>? SponsorshipRevoked { add { } remove { } }

    public Task InitializeAsync() => Task.CompletedTask;
    public string? GetPetRole(string petSyncId) => null;
    public bool OwnsASharedPet => false;
    public IReadOnlyList<string> CaregiverPetSyncIds => Array.Empty<string>();

    // No cloud ⇒ nothing to wait for and nothing sponsored, so the billing gate falls back
    // entirely to the local subscription/grant. Reporting AccessKnown=false here would hold
    // every paid surface open forever.
    public bool AccessKnown => true;
    public Billing.PetAccessInfo? GetPetAccess(string? petSyncId) => null;
    public Task<SyncOutcome> SyncNowAsync() => Task.FromResult(SyncOutcome.BackupDisabled);
    public void RequestSyncSoon() { }
    public void NotifyAppState(bool foreground) { }
    public Task<SyncOutcome> EnableBackupAsync() => Task.FromResult(SyncOutcome.BackupDisabled);
    public Task DisableBackupAsync() => Task.CompletedTask;
    public Task<SignOutImpact> PrepareSignOutAsync()
        => Task.FromResult(new SignOutImpact(Array.Empty<string>(), 0));
    public Task<int> SignOutTeardownAsync() => Task.FromResult(0);
    public Task DeleteCloudDataAsync() => Task.CompletedTask;
    public Task DeleteAccountAsync() => Task.CompletedTask;
}
