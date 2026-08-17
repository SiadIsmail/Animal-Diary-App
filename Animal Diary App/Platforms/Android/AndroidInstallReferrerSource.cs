namespace Animal_Diary_App.Platforms.Android;

using System.Diagnostics;
using global::Android.Content;
using Animal_Diary_App.Data.Services.Data.Device;
using Xamarin.Android.InstallReferrer.Api;

/// <summary>
/// Reads Google Play's install referrer through the Play Install Referrer Library
/// (<c>com.android.installreferrer:installreferrer</c>, via Microsoft's binding). This is
/// the only file that touches an install-referrer type — everything above it sees the plain
/// <see cref="IInstallReferrerSource"/>.
///
/// <para>The library is a bound service with a callback, not an async call, so the listener
/// below is adapted onto a <see cref="TaskCompletionSource{TResult}"/>. Three things make
/// that adaptation load-bearing rather than incidental:</para>
///
/// <list type="bullet">
///   <item><b>The callback can fire more than once.</b> A service that disconnects and
///   reconnects re-enters the listener, and a second <c>SetResult</c> on the same source
///   throws — on a background thread, during launch. Hence <c>TrySetResult</c> throughout.</item>
///   <item><b>It can fire never.</b> A device with a broken or absent Play Store can leave
///   the connection pending forever, so the wait is time-bounded and the launch path
///   continues without an answer.</item>
///   <item><b>The client must be closed.</b> <c>EndConnection</c> in a finally, or the
///   service binding leaks for the life of the process.</item>
/// </list>
/// </summary>
public sealed class AndroidInstallReferrerSource : IInstallReferrerSource
{
    // Generous enough for a cold Play Services start, short enough that a broken store
    // cannot hold up the first sync behind it.
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    public async Task<string?> GetInstallReferrerAsync()
    {
        InstallReferrerClient? client = null;
        try
        {
            var context = global::Android.App.Application.Context;
            client = InstallReferrerClient.NewBuilder(context)?.Build();
            if (client == null)
            {
                Debug.WriteLine("[Referrer] could not build an install referrer client");
                return null;
            }

            var ready = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            client.StartConnection(new Listener(ready));

            var finished = await Task.WhenAny(ready.Task, Task.Delay(Timeout));
            if (finished != ready.Task)
            {
                Debug.WriteLine("[Referrer] Play install referrer timed out");
                return null;
            }

            var response = await ready.Task;
            if (response != InstallReferrerClient.InstallReferrerResponse.Ok)
            {
                // FeatureNotSupported (old Play Store) and ServiceUnavailable (no Play at
                // all, e.g. a sideload or a non-Google device) are ordinary outcomes here,
                // not errors: those installs simply carry no referrer.
                Debug.WriteLine($"[Referrer] Play install referrer unavailable (response {response})");
                return null;
            }

            var details = client.InstallReferrer;
            var referrer = details?.InstallReferrer;
            Debug.WriteLine($"[Referrer] raw install referrer: '{referrer}'");
            return string.IsNullOrWhiteSpace(referrer) ? null : referrer;
        }
        catch (Exception ex)
        {
            // Attribution is a marketing number on a launch path. It never gets to break
            // anything, and a device without Play Services must not even log noisily.
            Debug.WriteLine($"[Referrer] install referrer read failed: {ex.Message}");
            return null;
        }
        finally
        {
            try { client?.EndConnection(); }
            catch (Exception ex) { Debug.WriteLine($"[Referrer] EndConnection failed: {ex.Message}"); }
        }
    }

    private sealed class Listener : Java.Lang.Object, IInstallReferrerStateListener
    {
        private readonly TaskCompletionSource<int> _ready;

        public Listener(TaskCompletionSource<int> ready) => _ready = ready;

        public void OnInstallReferrerSetupFinished(int responseCode) => _ready.TrySetResult(responseCode);

        /// <summary>The service dropped before (or after) answering. Resolved as
        /// <c>ServiceUnavailable</c> rather than waiting out the timeout: the caller has a
        /// one-shot flag and nothing is gained by holding the launch path open for a
        /// connection that is gone. (The Java library's own SERVICE_DISCONNECTED constant is
        /// not surfaced by this binding, and the two mean the same thing to us — no answer.)</summary>
        public void OnInstallReferrerServiceDisconnected() =>
            _ready.TrySetResult(InstallReferrerClient.InstallReferrerResponse.ServiceUnavailable);
    }
}
