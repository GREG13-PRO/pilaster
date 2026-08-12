using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Pilaster.Core.Collections;

/// <summary>
/// Megfigyelhető lista, ami sok elemet egyetlen értesítéssel tud hozzáfűzni.
/// </summary>
/// <remarks>
/// <para>
/// A sima <see cref="ObservableCollection{T}"/> minden egyes
/// <c>Add</c> hívásnál külön <c>CollectionChanged</c> eseményt vált ki, amit a
/// WPF <c>CollectionView</c> végigfuttat és érvényteleníti a méretezést. Egy
/// 200 000 elemű mappánál ez önmagában másodpercekig tart — a virtualizáció
/// ezen nem segít, mert nem a rajzolás a szűk keresztmetszet, hanem az
/// értesítések száma.
/// </para>
/// <para>
/// Ezért az <see cref="AddRange"/> egyetlen <c>Reset</c> értesítést küld.
/// Betöltés közben ez nem jár észlelhető mellékhatással: a görgetés a lista
/// tetején áll, kijelölés pedig még nincs.
/// </para>
/// </remarks>
public sealed class RangeObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    public RangeObservableCollection()
    {
    }

    public RangeObservableCollection(IEnumerable<T> collection)
        : base(collection)
    {
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_suppressNotifications)
        {
            return;
        }

        base.OnCollectionChanged(e);
    }

    /// <summary>Sok elem hozzáfűzése egyetlen értesítéssel.</summary>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var added = false;
        _suppressNotifications = true;

        try
        {
            foreach (var item in items)
            {
                Items.Add(item);
                added = true;
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        if (added)
        {
            RaiseReset();
        }
    }

    /// <summary>A lista teljes cseréje egyetlen értesítéssel.</summary>
    public void Reset(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _suppressNotifications = true;

        try
        {
            Items.Clear();

            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        RaiseReset();
    }

    private void RaiseReset()
    {
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
        base.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
