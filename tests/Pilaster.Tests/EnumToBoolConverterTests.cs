using System.Globalization;
using System.Windows.Data;
using Pilaster.App.Converters;
using Pilaster.Core.Settings;

namespace Pilaster.Tests;

/// <summary>
/// A gyorsgomb-típus rádiógombjait kiszolgáló konverter.
/// </summary>
/// <remarks>
/// Ezek a tesztek egy valódi hibára válaszolnak: a beállításfájlban egyszer
/// <c>"kind": "File"</c> szerepelt az első gyorsgombnál, holott a felület
/// helyesen „Mappa"-t mutatott, és mappát is hozott létre. A korábbi megoldás
/// két rádiógombot kötött egyetlen <c>bool</c>-ra, invertáló kétirányú
/// konverterrel — a rádiócsoport a társ gombot magától kikapcsolja, és azt a
/// kétirányú kötés azonnal vissza is írta a modellbe.
/// </remarks>
public class EnumToBoolConverterTests
{
    private static readonly EnumToBoolConverter Converter = new();
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    [Fact]
    public void Convert_JeloliAKivalasztottErteket()
    {
        Assert.True((bool)Converter.Convert(QuickActionKind.Folder, typeof(bool), "Folder", Culture));
        Assert.False((bool)Converter.Convert(QuickActionKind.Folder, typeof(bool), "File", Culture));
    }

    [Fact]
    public void Convert_KisNagybetutolFuggetlen()
    {
        Assert.True((bool)Converter.Convert(QuickActionKind.File, typeof(bool), "file", Culture));
    }

    [Fact]
    public void ConvertBack_BekapcsolaskorAzEnumotAdja()
    {
        var result = Converter.ConvertBack(true, typeof(QuickActionKind), "File", Culture);

        Assert.Equal(QuickActionKind.File, result);
    }

    /// <summary>
    /// A hiba lényege: a kikapcsolás nem a felhasználó szándéka, hanem a
    /// rádiócsoport mellékhatása, ezért a modellhez nem szabad hozzányúlnia.
    /// </summary>
    [Fact]
    public void ConvertBack_KikapcsolaskorNemIrjaFelulAModellt()
    {
        var result = Converter.ConvertBack(false, typeof(QuickActionKind), "Folder", Culture);

        Assert.Same(Binding.DoNothing, result);
    }

    /// <summary>
    /// A rádiócsoport tényleges viselkedésének leképezése: a „Fájl"-ra
    /// kattintás bekapcsolja a Fájlt ÉS kikapcsolja a Mappát. A végállapotnak
    /// Fájlnak kell lennie, függetlenül attól, milyen sorrendben érkeznek az
    /// értesítések — a régi megoldás épp ezen bukott el.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RadioCsoport_MindketSorrendbenHelyesVegallapot(bool kikapcsolasEloszor)
    {
        var kind = QuickActionKind.Folder;

        void Apply(bool isChecked, string parameter)
        {
            var value = Converter.ConvertBack(isChecked, typeof(QuickActionKind), parameter, Culture);

            if (value is QuickActionKind parsed)
            {
                kind = parsed;
            }
        }

        if (kikapcsolasEloszor)
        {
            Apply(false, "Folder");
            Apply(true, "File");
        }
        else
        {
            Apply(true, "File");
            Apply(false, "Folder");
        }

        Assert.Equal(QuickActionKind.File, kind);
    }
}
