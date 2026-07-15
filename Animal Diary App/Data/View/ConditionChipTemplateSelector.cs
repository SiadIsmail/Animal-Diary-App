namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Picks the Manage page's Conditions FlexLayout template for an
/// <see cref="IConditionChipItem"/>: the normal chip design for a
/// <see cref="ManageConditionChip"/>, the dashed Add design for the trailing
/// <see cref="AddConditionChipItem"/>. Templates are declared in ManagePetPage
/// resources. Adding a new chip kind = one template + one case here.
/// </summary>
public class ConditionChipTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ConditionTemplate { get; set; }
    public DataTemplate? AddTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        return item switch
        {
            AddConditionChipItem => AddTemplate,
            ManageConditionChip => ConditionTemplate,
            _ => null
        };
    }
}
