namespace Animal_Diary_App.Data.View;

/// <summary>The access-code sheet. Purely VM-driven — the host page sets its BindingContext
/// to the shared RedeemCodeSheetViewModel; there is no per-page logic here.</summary>
public partial class RedeemCodeSheetView : ContentView
{
    public RedeemCodeSheetView() => InitializeComponent();
}
