using System.Windows.Data;
using System.Windows.Markup;

namespace Pilaster.App.Localization;

/// <summary>
/// XAML kiterjesztés feliratokhoz: <c>Content="{loc:Loc Cmd_Back}"</c>.
/// </summary>
/// <remarks>
/// Kötést ad vissza, nem sima szöveget — ezért nyelvváltáskor a felirat magától
/// frissül, anélkül hogy a nézetet újra kellene építeni.
/// </remarks>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension()
    {
    }

    public LocExtension(string key) => Key = key;

    /// <summary>A felirat kulcsa a <c>Strings.resx</c>-ben.</summary>
    [ConstructorArgument("key")]
    public string Key { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = TranslationSource.Instance,
            Mode = BindingMode.OneWay,
        };

        return binding.ProvideValue(serviceProvider);
    }
}
