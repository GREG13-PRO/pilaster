using System.Resources;

namespace Pilaster.App.Resources;

/// <summary>
/// Hozzáférés a beágyazott feliratokhoz.
/// </summary>
/// <remarks>
/// Szándékosan nincs kulcsonkénti generált tulajdonság: a feliratokat mindig a
/// <c>TranslationSource</c> olvassa kulcs alapján, hogy a nyelv futásidőben
/// váltható legyen. Így új felirat felvételéhez elég a <c>.resx</c>-et bővíteni.
/// </remarks>
internal static class Strings
{
    private static readonly Lazy<ResourceManager> Manager = new(() =>
        new ResourceManager("Pilaster.App.Resources.Strings", typeof(Strings).Assembly));

    public static ResourceManager ResourceManager => Manager.Value;
}
