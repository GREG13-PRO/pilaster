using Pilaster.Core.Settings;

namespace Pilaster.Tests;

/// <summary>
/// A billentyűkiosztás átnevezése (F1) és a config-migráció: a felhasználó
/// beállítása NEM veszhet el az idegen terméknév kiváltásakor.
/// </summary>
public class KeymapPresetTests
{
    [Theory]
    [InlineData("totalcommander")]
    [InlineData("TotalCommander")]
    [InlineData("tc")]
    [InlineData("TC")]
    [InlineData("total_commander")]
    [InlineData("total commander")]
    [InlineData("total-commander")]
    public void Parse_RegiErtekekPilasterClassicreKepzodnek(string legacy)
    {
        Assert.Equal(KeymapPreset.PilasterClassic, KeymapPresetParser.Parse(legacy));
    }

    [Theory]
    [InlineData("pilaster-classic")]
    [InlineData("PilasterClassic")]
    [InlineData("classic")]
    public void Parse_UjNevekIsMukodnek(string value)
    {
        Assert.Equal(KeymapPreset.PilasterClassic, KeymapPresetParser.Parse(value));
    }

    [Theory]
    [InlineData("explorer")]
    [InlineData("pilaster-modern")]
    [InlineData("modern")]
    public void Parse_ModernValtozatok(string value)
    {
        Assert.Equal(KeymapPreset.Explorer, KeymapPresetParser.Parse(value));
    }

    /// <summary>
    /// Ismeretlen vagy hiányzó érték a MODERN kiosztásra esik vissza: az nem
    /// foglal le funkcióbillentyűket, tehát semmilyen megszokott működést nem
    /// ír felül a felhasználó tudta nélkül.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("valami-ismeretlen")]
    public void Parse_IsmeretlenErtekModernreEsikVissza(string? value)
    {
        Assert.Equal(KeymapPreset.Explorer, KeymapPresetParser.Parse(value));
    }

    [Fact]
    public void Parse_Egyedi()
    {
        Assert.Equal(KeymapPreset.Custom, KeymapPresetParser.Parse("custom"));
        Assert.Equal(KeymapPreset.Custom, KeymapPresetParser.Parse("egyedi"));
    }

    /// <summary>
    /// A v0.9-es logikai kapcsoló átvétele: bekapcsolt állapotból Classic,
    /// kikapcsoltból Modern lesz.
    /// </summary>
    [Fact]
    public void MigrateKeymap_BekapcsoltRegiKapcsolobolClassicLesz()
    {
#pragma warning disable CS0618 // A migráció tesztje épp a régi mezőt vizsgálja.
        var settings = new AppSettings { TotalCommanderKeybindingsEnabled = true };
#pragma warning restore CS0618

        settings.MigrateKeymap();

        Assert.Equal(KeymapPreset.PilasterClassic, settings.Keymap);
    }

    [Fact]
    public void MigrateKeymap_KikapcsoltRegiKapcsolobolModernLesz()
    {
#pragma warning disable CS0618
        var settings = new AppSettings { TotalCommanderKeybindingsEnabled = false };
#pragma warning restore CS0618

        settings.MigrateKeymap();

        Assert.Equal(KeymapPreset.Explorer, settings.Keymap);
    }

    /// <summary>
    /// A migráció NEM írhatja felül a már migrált (v1.0-ban kézzel
    /// beállított) értéket — különben minden indítás visszaállítaná a régit.
    /// </summary>
    [Fact]
    public void MigrateKeymap_NemIrjaFelulAMarBeallitottPresetet()
    {
#pragma warning disable CS0618
        var settings = new AppSettings { TotalCommanderKeybindingsEnabled = true };
#pragma warning restore CS0618

        settings.Keymap = KeymapPreset.Custom;
        settings.MigrateKeymap();

        Assert.Equal(KeymapPreset.Custom, settings.Keymap);
    }

    [Fact]
    public void ResourceKey_EgyikSemTartalmazIdegenTermeknevet()
    {
        foreach (var preset in Enum.GetValues<KeymapPreset>())
        {
            var key = preset.ResourceKey();

            Assert.StartsWith("Keymap_", key);
            Assert.DoesNotContain("commander", key, StringComparison.OrdinalIgnoreCase);
        }
    }
}
