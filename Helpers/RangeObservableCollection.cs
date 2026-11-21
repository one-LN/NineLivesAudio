using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace NineLivesAudio.Helpers;

/// <summary>
/// An <see cref="ObservableCollection{T}"/> with a bulk replace that raises a single
/// <see cref="NotifyCollectionChangedAction.Reset"/> notification.
/// <para>
/// Replacing a library page the naive way (<c>Clear()</c> + one <c>Add</c> per book)
/// raises N+1 events; the bound ItemsRepeater then re-runs its layout for every item,
/// which is what made big libraries stutter on refresh, search and library switching.
/// </para>
/// </summary>
public class RangeObservableCollection<T> : ObservableCollection<T>
{
    private static readonly PropertyChangedEventArgs CountChangedArgs = new("Count");
    private static readonly PropertyChangedEventArgs IndexerChangedArgs = new("Item[]");

    public void ReplaceRange(IReadOnlyList<T> items)
    {
        Items.Clear();
        for (var i = 0; i < items.Count; i++)
            Items.Add(items[i]);

        OnPropertyChanged(CountChangedArgs);
        OnPropertyChanged(IndexerChangedArgs);
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
