namespace Animal_Diary_App.Data.View;

using System.Linq;
using Animal_Diary_App.Data.View.Controls;
using Animal_Diary_App.Data.ViewModels;

public partial class WeightChartView : ContentView
{
    private readonly WeightChartDrawable drawable = new();
    private MainPageViewModel? vm;

    public WeightChartView()
    {
        InitializeComponent();
        ChartSurface.Drawable = drawable;
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();

        if (vm != null)
            vm.WeightChartUpdated -= OnChartUpdated;

        vm = (BindingContext as MainViewModel)?.MainPageVM;

        if (vm != null)
        {
            vm.WeightChartUpdated += OnChartUpdated;
            UpdateAndRedraw();
        }
    }

    private void OnChartUpdated() => Dispatcher.Dispatch(UpdateAndRedraw);

    private void UpdateAndRedraw()
    {
        if (vm == null)
            return;

        drawable.Values = vm.WeightChartData.Select(p => (double)p.Value).ToList();
        drawable.Min = vm.WeightAxisMin;
        drawable.Max = vm.WeightAxisMax;
        ChartSurface.Invalidate();
    }
}
