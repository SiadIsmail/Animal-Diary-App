namespace Animal_Diary_App.Helpers;

using System.Runtime.CompilerServices;

/// <summary>
/// Safety net for intentionally un-awaited work. A discarded task (`_ = FooAsync()`)
/// swallows its exception silently; an exception escaping an <c>async void</c>
/// handler crashes the process. Route both through <see cref="Forget"/> so a failed
/// DB read degrades to a logged error instead of a crash or a silent mystery.
/// </summary>
public static class TaskExtensions
{
    /// <summary>Observe a fire-and-forget task, logging any failure with the
    /// caller's location. Usage: <c>LoadAsync().Forget();</c></summary>
    public static async void Forget(
        this Task task,
        [CallerMemberName] string caller = "",
        [CallerFilePath] string file = "")
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Forget] {System.IO.Path.GetFileName(file)}.{caller} failed: {ex}");
        }
    }
}
