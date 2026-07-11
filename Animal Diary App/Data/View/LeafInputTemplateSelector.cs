namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.Models;
using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Picks the Tracker Hub input layout for a <see cref="TrackerLeaf"/> from its
/// <see cref="TrackerLeaf.TemplateKind"/>. Templates are declared in CalendarPage
/// resources. Mood gets the bespoke emoji+note editor; Weight reuses the numeric
/// editor; Volume reuses numeric too. Adding a new kind = one template + one case.
/// </summary>
public class LeafInputTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MoodTemplate { get; set; }
    public DataTemplate? NumericTemplate { get; set; }
    public DataTemplate? ScaleTemplate { get; set; }
    public DataTemplate? BooleanTemplate { get; set; }
    public DataTemplate? DoseTemplate { get; set; }
    public DataTemplate? TextTemplate { get; set; }
    public DataTemplate? EventTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not TrackerLeaf leaf)
            return null;

        return leaf.TemplateKind switch
        {
            InputKind.Mood => MoodTemplate,
            InputKind.Numeric => NumericTemplate,
            InputKind.Volume => NumericTemplate,
            InputKind.Scale => ScaleTemplate,
            InputKind.Boolean => BooleanTemplate,
            InputKind.Dose => DoseTemplate,
            InputKind.Text => TextTemplate,
            InputKind.Event => EventTemplate,
            _ => NumericTemplate
        };
    }
}
