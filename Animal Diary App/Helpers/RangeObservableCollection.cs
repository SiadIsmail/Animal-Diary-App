namespace Animal_Diary_App.Helpers;

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> that can be repopulated in bulk with a
/// single <see cref="NotifyCollectionChangedAction.Reset"/> notification instead of
/// one event per item. Refilling a bound list (dose checklist, mood ribbon, week
/// dots) with Clear() + per-item Add() triggers a layout pass for every element;
/// <see cref="ReplaceAll"/> collapses that to one.
/// </summary>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    public RangeObservableCollection() { }

    public RangeObservableCollection(IEnumerable<T> collection) : base(collection) { }

    /// <summary>Clear the list and add every item, raising just one Reset event.</summary>
    public void ReplaceAll(IEnumerable<T> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);

        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
