namespace Pilaster.Core.Navigation;

/// <summary>
/// Egy fül vissza/előre előzménye, böngésző-szemantikával.
/// </summary>
/// <remarks>
/// A lényegi szabály: ha visszalépés után új helyre navigálunk, az „előre"
/// ág eldobódik — pont úgy, ahogy a böngészőben. Ezért nem elég egyetlen
/// lista indexszel: két verem tisztábban fejezi ki a szándékot.
/// </remarks>
public sealed class NavigationHistory
{
    private readonly Stack<string> _back = new();
    private readonly Stack<string> _forward = new();

    /// <summary>Az aktuális hely, vagy <c>null</c>, ha még nem navigáltunk.</summary>
    public string? Current { get; private set; }

    public bool CanGoBack => _back.Count > 0;

    public bool CanGoForward => _forward.Count > 0;

    /// <summary>Az előzmény visszafelé, a legutóbbitól kezdve.</summary>
    public IReadOnlyCollection<string> BackEntries => _back;

    /// <summary>Új helyre lépés. Az „előre" ág eldobódik.</summary>
    public void Navigate(string path)
    {
        if (string.Equals(Current, path, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (Current is not null)
        {
            _back.Push(Current);
        }

        Current = path;
        _forward.Clear();
    }

    /// <summary>Egy lépés vissza. <c>null</c>, ha nincs hova.</summary>
    public string? GoBack()
    {
        if (_back.Count == 0)
        {
            return null;
        }

        if (Current is not null)
        {
            _forward.Push(Current);
        }

        Current = _back.Pop();
        return Current;
    }

    /// <summary>Egy lépés előre. <c>null</c>, ha nincs hova.</summary>
    public string? GoForward()
    {
        if (_forward.Count == 0)
        {
            return null;
        }

        if (Current is not null)
        {
            _back.Push(Current);
        }

        Current = _forward.Pop();
        return Current;
    }
}
