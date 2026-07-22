namespace Animal_Diary_App.Data.Services.Cloud;

using System.Text.Json;

/// <summary>One member of a shared pet, as the sharing sheet shows it. Email is
/// the only identity the product has; <see cref="IsMe"/> marks the caller's own
/// row (their remove action reads "Leave").</summary>
public sealed record PetMemberInfo(string UserId, string Role, string Email, bool IsMe)
{
    public bool IsOwner => Role == "owner";
}

/// <summary>
/// The sharing operations — thin calls onto the SECURITY DEFINER RPCs from
/// supabase/migrations/0006. Authorization (ownership, membership, rate limits)
/// lives entirely server-side; failures arrive as named errors bucketed into
/// <see cref="CloudErrorKind"/>. After a successful redeem, callers run a sync
/// to actually download the newly shared pet.
/// </summary>
public interface ICloudSharingService
{
    /// <summary>Mint a fresh single-use invite code (owner only). Returns "ABCD-1234".</summary>
    Task<string> CreateInviteAsync(string petSyncId);

    /// <summary>Join a pet by code. Returns the pet's cloud id.</summary>
    Task<string> RedeemInviteAsync(string code);

    Task<List<PetMemberInfo>> GetMembersAsync(string petSyncId);

    /// <summary>Remove a caregiver (owner) or yourself (leave).</summary>
    Task RemoveMemberAsync(string petSyncId, string userId);
}

public sealed class CloudSharingService : ICloudSharingService
{
    private readonly CloudHttp _http;
    private readonly ICloudAuthService _auth;

    public CloudSharingService(CloudHttp http, ICloudAuthService auth)
    {
        _http = http;
        _auth = auth;
    }

    public async Task<string> CreateInviteAsync(string petSyncId)
    {
        var session = await RequireSessionAsync();
        var doc = await _http.RpcAsync("create_pet_invite", new { p_pet = petSyncId }, session.AccessToken);
        return doc!.RootElement.GetString() ?? string.Empty;
    }

    public async Task<string> RedeemInviteAsync(string code)
    {
        var session = await RequireSessionAsync();
        var doc = await _http.RpcAsync("redeem_invite", new { p_code = code.Trim().ToUpperInvariant() }, session.AccessToken);
        return doc!.RootElement.GetString() ?? string.Empty;
    }

    public async Task<List<PetMemberInfo>> GetMembersAsync(string petSyncId)
    {
        var session = await RequireSessionAsync();
        var doc = await _http.RpcAsync("list_pet_members", new { p_pet = petSyncId }, session.AccessToken);

        var result = new List<PetMemberInfo>();
        foreach (var el in doc!.RootElement.EnumerateArray())
        {
            var userId = CloudJson.GetString(el, "user_id");
            result.Add(new PetMemberInfo(
                userId,
                CloudJson.GetString(el, "member_role"),
                CloudJson.GetString(el, "email"),
                IsMe: userId == session.UserId));
        }
        return result;
    }

    public async Task RemoveMemberAsync(string petSyncId, string userId)
    {
        var session = await RequireSessionAsync();
        await _http.RpcAsync("remove_pet_member", new { p_pet = petSyncId, p_user = userId }, session.AccessToken);
    }

    private async Task<CloudSession> RequireSessionAsync()
        => await _auth.GetSessionAsync()
           ?? throw new CloudException(CloudErrorKind.AuthExpired, 0, "not signed in");
}

/// <summary>Registered when <see cref="CloudConfig.Enabled"/> is false.</summary>
public sealed class NullCloudSharingService : ICloudSharingService
{
    private static CloudException Off() => new(CloudErrorKind.Other, 0, "cloud disabled");
    public Task<string> CreateInviteAsync(string petSyncId) => Task.FromException<string>(Off());
    public Task<string> RedeemInviteAsync(string code) => Task.FromException<string>(Off());
    public Task<List<PetMemberInfo>> GetMembersAsync(string petSyncId) => Task.FromException<List<PetMemberInfo>>(Off());
    public Task RemoveMemberAsync(string petSyncId, string userId) => Task.FromException(Off());
}
