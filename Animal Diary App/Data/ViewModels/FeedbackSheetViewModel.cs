namespace Animal_Diary_App.Data.ViewModels;

using System.Diagnostics;
using System.Windows.Input;
using Animal_Diary_App.Helpers;

/// <summary>
/// The "feedback or a problem" sheet at the bottom of the Care page. Two doors and
/// nothing else: the Discord community, or a direct email to the two people who
/// build this. Deliberately not a form: a report typed into a text box inside a
/// broken app is the one message most likely to be lost, and neither door needs an
/// account, a ticket number, or a reply we can't promise.
/// </summary>
public sealed class FeedbackSheetViewModel : BaseViewModel
{
    /// <summary>The community invite. Same link the Settings panel uses.</summary>
    public const string DiscordUrl = "https://discord.gg/PAcarwtpYy";

    /// <summary>The mailbox both of us read.</summary>
    public const string ContactEmail = "hello@felova.app";

    public FeedbackSheetViewModel()
    {
        OpenCommand = new Command(() => IsPresented = true);
        DismissCommand = new Command(() => IsPresented = false);
        // Both doors close the sheet first: the launcher hands control to another app,
        // and coming back to a still-open sheet reads as "nothing happened".
        OpenDiscordCommand = new Command(async () =>
        {
            IsPresented = false;
            await OpenDiscordAsync();
        });
        OpenEmailCommand = new Command(async () =>
        {
            IsPresented = false;
            await OpenEmailAsync();
        });
    }

    private bool _isPresented;
    public bool IsPresented { get => _isPresented; set => SetProperty(ref _isPresented, value); }

    /// <summary>Shown under the email row so the address is readable (and copyable by
    /// eye) even where no mail app is installed to launch. Instance property, not
    /// static: compiled bindings can't resolve a static one.</summary>
    public string EmailAddress => ContactEmail;

    public ICommand OpenCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand OpenDiscordCommand { get; }
    public ICommand OpenEmailCommand { get; }

    private static async Task OpenDiscordAsync()
    {
        try
        {
            await Launcher.OpenAsync(new Uri(DiscordUrl));
        }
        catch (Exception ex)
        {
            // No browser, or the launch was cancelled. Nothing to report: the sheet
            // is already closed and the address is on screen behind it.
            Debug.WriteLine($"[Feedback] Discord launch failed: {ex}");
        }
    }

    private static async Task OpenEmailAsync()
    {
        var subject = LocalizationManager.Instance.GetString("Feedback_EmailSubject");

        try
        {
            await Email.Default.ComposeAsync(new EmailMessage
            {
                Subject = subject,
                To = new List<string> { ContactEmail },
            });
            return;
        }
        catch (Exception ex)
        {
            // FeatureNotSupportedException on desktop, or no mail app on Android.
            // Fall through to the mailto: handler, which the OS may still resolve.
            Debug.WriteLine($"[Feedback] compose failed, trying mailto: {ex}");
        }

        try
        {
            await Launcher.OpenAsync(new Uri($"mailto:{ContactEmail}?subject={Uri.EscapeDataString(subject)}"));
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[Feedback] mailto launch failed: {ex}");
        }
    }
}
