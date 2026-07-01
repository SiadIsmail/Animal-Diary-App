namespace Animal_Diary_App.Data.View.Controls;

using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Animal_Diary_App.Data.ViewModels;

/// <summary>
/// A lightweight 7-day week-strip calendar. Pure UI: it renders the week
/// containing <see cref="SelectedDate"/> with prev/next navigation, today/
/// selected highlighting, and activity dots. It owns no business logic and
/// knows nothing about pets or medications — the ViewModel supplies
/// <see cref="Activities"/> and consumes <see cref="SelectedDate"/> through a
/// bindable property. Weeks start on Monday.
/// </summary>
public partial class WeekCalendarView : ContentView
{
    private const int CellCount = 7;
    private const int MaxDotsPerDay = 4;

    // Dot colours (reuse the app palette).
    private static readonly Brush MedicationColor = new SolidColorBrush(Color.FromArgb("#9B6FCC"));
    private static readonly Brush WeightColor = new SolidColorBrush(Color.FromArgb("#2E9B8F"));
    private static readonly Brush MoodColor = new SolidColorBrush(Color.FromArgb("#E8A13A"));
    private static readonly Brush Transparent = Brush.Transparent;
    private static readonly IReadOnlyList<CalendarDot> NoDots = new List<CalendarDot>();

    public WeekCalendarView()
    {
        InitializeComponent();
        SelectDayCommand = new Command<CalendarDayCell>(OnDaySelected);
        BuildCells();
    }

    // ── Cells exposed to the grid ────────────────────────────────────────
    public ObservableCollection<CalendarDayCell> Cells { get; } = new();

    public ICommand SelectDayCommand { get; }

    private string weekLabel = string.Empty;
    public string WeekLabel
    {
        get => weekLabel;
        private set
        {
            if (weekLabel != value)
            {
                weekLabel = value;
                OnPropertyChanged();
            }
        }
    }

    // ── Bindable properties ──────────────────────────────────────────────
    public static readonly BindableProperty SelectedDateProperty = BindableProperty.Create(
        nameof(SelectedDate), typeof(DateTime), typeof(WeekCalendarView),
        defaultValue: DateTime.Today, defaultBindingMode: BindingMode.TwoWay,
        propertyChanged: OnSelectedDateChanged);

    public DateTime SelectedDate
    {
        get => (DateTime)GetValue(SelectedDateProperty);
        set => SetValue(SelectedDateProperty, value);
    }

    public static readonly BindableProperty ActivitiesProperty = BindableProperty.Create(
        nameof(Activities), typeof(IEnumerable<CalendarActivity>), typeof(WeekCalendarView),
        propertyChanged: OnActivitiesChanged);

    public IEnumerable<CalendarActivity>? Activities
    {
        get => (IEnumerable<CalendarActivity>?)GetValue(ActivitiesProperty);
        set => SetValue(ActivitiesProperty, value);
    }

    // ── Navigation: shift the selection (and therefore the week) by ±7 days ─
    private void OnPrevWeek(object? sender, EventArgs e) => SelectedDate = SelectedDate.AddDays(-7);
    private void OnNextWeek(object? sender, EventArgs e) => SelectedDate = SelectedDate.AddDays(7);

    private void OnDaySelected(CalendarDayCell? cell)
    {
        if (cell != null)
            SelectedDate = cell.Date;
    }

    // ── Property-changed plumbing ────────────────────────────────────────
    private static void OnSelectedDateChanged(BindableObject bindable, object oldValue, object newValue)
        => ((WeekCalendarView)bindable).BuildCells();

    private static void OnActivitiesChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (WeekCalendarView)bindable;
        if (oldValue is INotifyCollectionChanged oldObservable)
            oldObservable.CollectionChanged -= view.OnActivitiesCollectionChanged;
        if (newValue is INotifyCollectionChanged newObservable)
            newObservable.CollectionChanged += view.OnActivitiesCollectionChanged;
        view.RefreshDots();
    }

    private void OnActivitiesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshDots();

    // ── Rendering ────────────────────────────────────────────────────────
    /// <summary>Rebuild the 7 cells for the week containing the selection.</summary>
    private void BuildCells()
    {
        var start = StartOfWeek(SelectedDate);
        WeekLabel = FormatWeek(start);

        var today = DateTime.Now.Date;
        var selected = SelectedDate.Date;
        var lookup = BuildActivityLookup();

        if (Cells.Count != CellCount)
        {
            Cells.Clear();
            for (int i = 0; i < CellCount; i++)
                Cells.Add(new CalendarDayCell());
        }

        for (int i = 0; i < CellCount; i++)
        {
            var date = start.AddDays(i);
            var cell = Cells[i];
            cell.Date = date;
            cell.DayNumber = date.Day.ToString();
            cell.WeekdayLabel = date.ToString("ddd", CultureInfo.CurrentCulture);
            cell.IsToday = date == today;
            cell.IsSelected = date == selected;
            cell.SetDots(lookup.TryGetValue(date, out var dots) ? dots : NoDots);
        }
    }

    /// <summary>Refresh only the dots on existing cells (activities changed).</summary>
    private void RefreshDots()
    {
        if (Cells.Count != CellCount)
        {
            BuildCells();
            return;
        }

        var lookup = BuildActivityLookup();
        foreach (var cell in Cells)
            cell.SetDots(lookup.TryGetValue(cell.Date, out var dots) ? dots : NoDots);
    }

    private Dictionary<DateTime, List<CalendarDot>> BuildActivityLookup()
    {
        var map = new Dictionary<DateTime, List<CalendarDot>>();
        if (Activities == null)
            return map;

        // One dot per (date, type, state) keeps dense days readable and bounds
        // a day to at most four dots.
        var seen = new HashSet<(DateTime, CalendarActivityType, CalendarActivityState)>();
        foreach (var activity in Activities)
        {
            var date = activity.Date.Date;
            if (!seen.Add((date, activity.Type, activity.State)))
                continue;

            var dot = MakeDot(activity.Type, activity.State);
            if (dot == null)
                continue;

            if (!map.TryGetValue(date, out var list))
                map[date] = list = new List<CalendarDot>();
            if (list.Count < MaxDotsPerDay)
                list.Add(dot);
        }

        return map;
    }

    private static CalendarDot? MakeDot(CalendarActivityType type, CalendarActivityState state)
    {
        var color = type switch
        {
            CalendarActivityType.Medication => MedicationColor,
            CalendarActivityType.Weight => WeightColor,
            CalendarActivityType.Mood => MoodColor,
            _ => null   // Symptoms / VetVisit have no visual yet.
        };
        if (color == null)
            return null;

        // Scheduled → hollow ring; Completed → filled dot.
        return state == CalendarActivityState.Completed
            ? new CalendarDot { Fill = color, Stroke = color, StrokeThickness = 0 }
            : new CalendarDot { Fill = Transparent, Stroke = color, StrokeThickness = 1.5 };
    }

    /// <summary>Monday on/before the given date.</summary>
    private static DateTime StartOfWeek(DateTime date)
    {
        var d = date.Date;
        int offset = ((int)d.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return d.AddDays(-offset);
    }

    private static string FormatWeek(DateTime start)
    {
        var end = start.AddDays(6);
        if (start.Year != end.Year)
            return $"{start:d MMM yyyy} – {end:d MMM yyyy}";
        if (start.Month != end.Month)
            return $"{start:d MMM} – {end:d MMM yyyy}";
        return $"{start.Day} – {end.Day} {start:MMM yyyy}";
    }
}

/// <summary>One day cell in the week strip. Persistent + observable so a week
/// switch or selection change mutates cells in place instead of rebuilding views.</summary>
public sealed class CalendarDayCell : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    public DateTime Date { get; set; }

    private string dayNumber = string.Empty;
    public string DayNumber { get => dayNumber; set => Set(ref dayNumber, value); }

    private string weekdayLabel = string.Empty;
    public string WeekdayLabel { get => weekdayLabel; set => Set(ref weekdayLabel, value); }

    private bool isToday;
    public bool IsToday { get => isToday; set => Set(ref isToday, value); }

    private bool isSelected;
    public bool IsSelected { get => isSelected; set => Set(ref isSelected, value); }

    private IReadOnlyList<CalendarDot> dots = new List<CalendarDot>();
    public IReadOnlyList<CalendarDot> Dots { get => dots; private set => Set(ref dots, value); }

    public void SetDots(IReadOnlyList<CalendarDot> value) => Dots = value;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}

/// <summary>A single rendered dot. Filled = solid fill; scheduled = stroke-only ring.</summary>
public sealed class CalendarDot
{
    public Brush Fill { get; init; } = Brush.Transparent;
    public Brush Stroke { get; init; } = Brush.Transparent;
    public double StrokeThickness { get; init; }
}
