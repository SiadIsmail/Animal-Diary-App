namespace Animal_Diary_App.Data.View;

/// <summary>The subscribe sheet (yearly + monthly + restore). Purely VM-driven — the
/// host page sets its BindingContext to the shared SubscribeSheetViewModel; there is no
/// per-page logic here.</summary>
public partial class SubscribeSheetView : ContentView
{
    public SubscribeSheetView() => InitializeComponent();
}
