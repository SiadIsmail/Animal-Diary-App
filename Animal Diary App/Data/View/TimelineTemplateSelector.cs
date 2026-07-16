namespace Animal_Diary_App.Data.View;

using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// Picks a card layout for one <see cref="TimelineItem"/> on the Journal's single
/// chronological timeline: Mood gets the warm-paper washi-note card, every other
/// kind (Weight, Glucose, Appetite, Seizure, Dose) shares the standard title + sub
/// card. Templates are declared in CalendarPage resources. The selector never
/// affects ordering — that's purely by time in the ViewModel.
/// </summary>
public class TimelineTemplateSelector : DataTemplateSelector
{
    public DataTemplate? MoodTemplate { get; set; }
    public DataTemplate? StandardTemplate { get; set; }

    protected override DataTemplate? OnSelectTemplate(object item, BindableObject container)
    {
        if (item is not TimelineItem t)
            return null;

        return t.Kind == TimelineKind.Mood ? MoodTemplate : StandardTemplate;
    }
}
