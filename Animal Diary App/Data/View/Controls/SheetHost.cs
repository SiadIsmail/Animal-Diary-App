namespace Animal_Diary_App.Data.View.Controls;

// This namespace contains a "View" segment, which shadows the MAUI View type.
using View = Microsoft.Maui.Controls.View;

/// <summary>
/// A placeholder for one overlay sheet that builds its body the first time it is
/// actually needed, instead of at page-inflation time.
///
/// <para><b>Why this exists.</b> Every sheet a page can show was declared inline in
/// that page's XAML, and the three tab pages are all constructed when the Shell is.
/// That put roughly 2,240 XAML elements of export sheet, cloud sheet, subscribe
/// sheet, dev panel, settings panel and seven Journal input sheets — about 75% of
/// everything the app inflates — on the launch path, for surfaces a typical session
/// opens zero times. Shell then creates the native views for a tab on first
/// selection, so the same weight was paid again as a stutter on the first tap of
/// Journal and of Pets.
/// </para>
///
/// <para><b>What it does not change.</b> Once realised, the body stays in the visual
/// tree for good — translated off-screen by <see cref="FelovaBottomSheet"/>, never
/// collapsed with <c>IsVisible</c>. That invariant is the one the sheet's own header
/// warns about (content populated on open rendered empty on first show when the view
/// had never been laid out), and lazy creation does not weaken it: a sheet is either
/// absent entirely, or present and laid out — never present and collapsed.
/// </para>
///
/// <para><b>Two triggers.</b> The host normally realises when the page calls
/// <see cref="PreloadAll"/> after its data load has settled — deliberately after, so
/// the inflation never competes with the queries and list rebuilds the person is
/// actually waiting for, and staggered through <see cref="SheetInflationQueue"/> so
/// ten sheets don't land in one frame. <see cref="Presented"/> is the safety net for
/// anyone who opens a sheet before its turn comes up.
/// </para>
///
/// <para>Usage — one line, and the body's type stays a compile-time reference:
/// <code>
/// &lt;controls:SheetHost Grid.RowSpan="2"
///                     BindingContext="{Binding CloudVM}"
///                     Presented="{Binding IsPresented}"
///                     SheetTemplate="{DataTemplate view:CloudSheetView}"/&gt;
/// </code>
/// </para>
/// </summary>
public sealed class SheetHost : ContentView
{
    private bool _realised;

    public SheetHost()
    {
        // Nothing to hit until there is a body. Cleared on realise so the sheet's own
        // InputTransparent binding governs from then on, exactly as it did when the
        // sheet was a direct child of the page.
        InputTransparent = true;
    }

    // ── SheetTemplate ────────────────────────────────────────────────────────────
    public static readonly BindableProperty SheetTemplateProperty = BindableProperty.Create(
        nameof(SheetTemplate), typeof(DataTemplate), typeof(SheetHost), null);

    /// <summary>The sheet body to build, as <c>{DataTemplate view:SomeSheetView}</c>.
    /// A template rather than a <c>Type</c> deliberately: it is the same shape Shell
    /// uses for lazy pages, and it keeps the body a compile-time reference instead of
    /// a reflective construction the linker would have to be told about.</summary>
    public DataTemplate? SheetTemplate
    {
        get => (DataTemplate?)GetValue(SheetTemplateProperty);
        set => SetValue(SheetTemplateProperty, value);
    }

    // ── Presented ────────────────────────────────────────────────────────────────
    public static readonly BindableProperty PresentedProperty = BindableProperty.Create(
        nameof(Presented), typeof(bool), typeof(SheetHost), false,
        propertyChanged: (bindable, _, value) =>
        {
            if (value is true)
                ((SheetHost)bindable).Realise();
        });

    /// <summary>Bind to the sheet view model's own presented flag. Read only as a
    /// build trigger — the sheet inside binds the same flag for its animation, so
    /// nothing here needs to pass it on.</summary>
    public bool Presented
    {
        get => (bool)GetValue(PresentedProperty);
        set => SetValue(PresentedProperty, value);
    }

    /// <summary>Build the body if it isn't built yet. Idempotent and cheap to call.</summary>
    public void Realise()
    {
        if (_realised)
            return;
        _realised = true;

        if (SheetTemplate is not { } template)
            return;

        try
        {
            Content = template.CreateContent() as View;
            // The body is here; let it take its own touches again.
            InputTransparent = false;
        }
        catch (Exception ex)
        {
            // A sheet that fails to build must leave the page usable rather than take
            // the app down — the person can still do everything except open this one.
            System.Diagnostics.Debug.WriteLine($"[SheetHost] sheet failed to build: {ex}");
        }
    }

    /// <summary>Queue every sheet on this page for staggered background building.
    /// Call once per page, after its data load has completed.</summary>
    public static void PreloadAll(Page page)
    {
        foreach (var host in Find(page, depth: 0))
            SheetInflationQueue.Enqueue(page.Dispatcher, host.Realise);
    }

    // The hosts are direct children of each page's root Grid, but a shallow recursive
    // walk costs nothing and survives someone wrapping them in a container later.
    private static IEnumerable<SheetHost> Find(IView? view, int depth)
    {
        if (view is null || depth > 3)
            yield break;

        if (view is SheetHost host)
        {
            yield return host;
            yield break;
        }

        IEnumerable<IView>? children = view switch
        {
            Layout layout => layout.Children,
            ContentPage page when page.Content is not null => new IView[] { page.Content },
            ContentView content when content.Content is not null => new IView[] { content.Content },
            _ => null,
        };

        if (children is null)
            yield break;

        foreach (var child in children)
            foreach (var found in Find(child, depth + 1))
                yield return found;
    }
}

/// <summary>
/// Drains queued sheet builds one per dispatcher turn.
///
/// <para>The point is the spacing, not the deferral. Ten sheets realised in one go is
/// the same stall this whole change exists to remove, just moved a second later. One
/// body per tick keeps each chunk (40–90 elements) comfortably inside a frame.</para>
/// </summary>
internal static class SheetInflationQueue
{
    /// <summary>Gap between builds. Long enough that a build and the frame it dirties
    /// are finished before the next one starts, short enough that a page's sheets are
    /// all ready within about a second and a half.</summary>
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(120);

    private static readonly Queue<Action> Pending = new();
    private static bool _draining;

    /// <summary>Main thread only — the queue is unsynchronized because every caller is
    /// a page's post-load hook, which already runs there.</summary>
    public static void Enqueue(IDispatcher dispatcher, Action work)
    {
        Pending.Enqueue(work);
        if (_draining)
            return;

        _draining = true;
        dispatcher.DispatchDelayed(Step, () => Drain(dispatcher));
    }

    private static void Drain(IDispatcher dispatcher)
    {
        if (Pending.Count == 0)
        {
            _draining = false;
            return;
        }

        try
        {
            Pending.Dequeue()();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[SheetHost] preload failed: {ex}");
        }

        dispatcher.DispatchDelayed(Step, () => Drain(dispatcher));
    }
}
