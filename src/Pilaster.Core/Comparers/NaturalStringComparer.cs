using System.Runtime.InteropServices;

namespace Pilaster.Core.Comparers;

/// <summary>
/// Fájlnév-rendezés a Windows Explorer szabályai szerint.
/// </summary>
/// <remarks>
/// Sima szöveges rendezéssel a „kép10.jpg" a „kép9.jpg" ELÉ kerülne, mert az
/// „1" karakter megelőzi a „9"-et. Az Explorer ehelyett a számjegysorozatokat
/// számként hasonlítja össze. Ugyanazt a natív függvényt hívjuk, amit a shell
/// is használ, így a sorrend pontosan egyezik — nem „majdnem egyezik".
/// </remarks>
public sealed partial class NaturalStringComparer : IComparer<string>
{
    public static NaturalStringComparer Instance { get; } = new();

    private NaturalStringComparer()
    {
    }

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int StrCmpLogicalW(string x, string y);

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }

        if (x is null)
        {
            return -1;
        }

        if (y is null)
        {
            return 1;
        }

        return StrCmpLogicalW(x, y);
    }
}
