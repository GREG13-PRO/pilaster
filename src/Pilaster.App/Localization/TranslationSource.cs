using System.ComponentModel;
using System.Globalization;
using System.Resources;
using Pilaster.App.Resources;

namespace Pilaster.App.Localization;

/// <summary>
/// Futásidőben váltható fordítási forrás.
/// </summary>
/// <remarks>
/// <para>
/// A szokásos <c>x:Static</c> alapú resx-kötés statikus: nyelvváltáskor csak
/// újraindítással frissül. Itt ehelyett minden felirat egy indexelt kötésen
/// (<c>[Kulcs]</c>) keresztül olvas. Nyelvváltáskor egyetlen
/// <c>PropertyChanged("Item[]")</c> értesítés minden kötést újraértékeltet,
/// így az egész felület azonnal átvált — újraindítás nélkül.
/// </para>
/// <para>
/// Ez a terv szerinti 30+ nyelvhez is skálázódik: új nyelvhez csak egy
/// <c>Strings.&lt;kód&gt;.resx</c> kell, kódmódosítás nélkül.
/// </para>
/// </remarks>
public sealed class TranslationSource : INotifyPropertyChanged
{
    private static readonly Lazy<TranslationSource> Lazy = new(() => new TranslationSource());

    /// <summary>Az alkalmazás egyetlen fordítási forrása.</summary>
    public static TranslationSource Instance => Lazy.Value;

    private readonly ResourceManager _resources = Strings.ResourceManager;
    private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

    private TranslationSource()
    {
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Egy felirat kulcs alapján. Hiányzó kulcsnál magát a kulcsot adja vissza,
    /// hogy fejlesztéskor azonnal látszódjon a hiány, ne üres felirat legyen.
    /// </summary>
    public string this[string key] =>
        _resources.GetString(key, _currentCulture) ?? $"!{key}!";

    /// <summary>
    /// Az aktuális nyelv. Beállításakor az egész felület újrarajzolja a feliratait.
    /// </summary>
    public CultureInfo CurrentCulture
    {
        get => _currentCulture;
        set
        {
            if (_currentCulture.Name == value.Name)
            {
                return;
            }

            _currentCulture = value;

            CultureInfo.CurrentUICulture = value;

            // A dátum- és számformázás is kövesse a választott nyelvet.
            CultureInfo.CurrentCulture = value;

            // Az "Item[]" a WPF által értett jelzés: minden indexelt kötés frissüljön.
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CurrentCulture)));
        }
    }

    /// <summary>Nyelvváltás kultúrakód alapján (pl. <c>"hu"</c>, <c>"en"</c>).</summary>
    public void SetLanguage(string cultureName) =>
        CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
}
