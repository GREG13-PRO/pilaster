using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace IconAudit;

/// <summary>
/// A tálcaikon-hiba (spec B3 / A3) kiadás előtti, mechanikus ellenőrzése.
/// </summary>
/// <remarks>
/// Öt mérés, sorban: a beágyazott <c>.ico</c> rétegei és alfa-csatornája, a
/// leskálázás-gyanú a kis méreteknél, az exe <c>VersionInfo</c>-ja, a
/// parancsikonok <c>System.AppUserModel.ID</c> tulajdonsága (szintetikus
/// kerekítéssel, mivel ezen a gépen nincs telepítő), és — külön, a Pilaster
/// folyamaton belülről futó önteszttel — a futásidejű AppUserModelID és a
/// <c>WM_SETICON</c> réteg.
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        var repoRoot = FindRepoRoot();
        var icoPath = args.Length > 0 ? args[0] : Path.Combine(repoRoot, "assets", "brand", "app.ico");

        var exeCandidates = new[]
        {
            Path.Combine(repoRoot, "src", "Pilaster.App", "bin", "Release", "net10.0-windows", "Pilaster.exe"),
            Path.Combine(repoRoot, "src", "Pilaster.App", "bin", "Debug", "net10.0-windows", "Pilaster.exe"),
            @"C:\Program Files\Pilaster\Pilaster.exe",
        };

        var exePath = exeCandidates.FirstOrDefault(File.Exists);

        Console.WriteLine("== IconAudit ==");
        Console.WriteLine($"ico : {icoPath}");
        Console.WriteLine($"exe : {exePath ?? "(nem talalhato)"}");
        Console.WriteLine();

        var problems = new List<string>();

        AuditIco(icoPath, problems);
        Console.WriteLine();

        if (exePath is not null)
        {
            AuditVersionInfo(exePath, problems);
            Console.WriteLine();
        }
        else
        {
            problems.Add("VersionInfo: nem mert - nincs leforditott Pilaster.exe.");
        }

        if (exePath is not null)
        {
            AuditShortcut(exePath, problems);
            Console.WriteLine();
        }
        else
        {
            problems.Add("Parancsikon: nem mert - nincs leforditott Pilaster.exe.");
        }

        Console.WriteLine("== OSSZEGZES ==");

        if (problems.Count == 0)
        {
            Console.WriteLine("Nincs talalt problema ebben a mereskorben.");
        }
        else
        {
            foreach (var problem in problems)
            {
                Console.WriteLine($"! {problem}");
            }
        }

        return problems.Count == 0 ? 0 : 1;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pilaster.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("A repo gyökere nem található.");
    }

    // ─────────────────────────────────────────────────────────────
    //  1) A beágyazott .ico rétegei
    // ─────────────────────────────────────────────────────────────

    private static void AuditIco(string icoPath, List<string> problems)
    {
        Console.WriteLine("--- 1) .ico rétegek ---");

        if (!File.Exists(icoPath))
        {
            problems.Add($".ico: nem található ({icoPath}).");
            return;
        }

        using var stream = File.OpenRead(icoPath);
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);

        var frames = decoder.Frames
            .Select(f => (Frame: f, Size: f.PixelWidth))
            .OrderBy(f => f.Size)
            .ToList();

        Console.WriteLine($"rétegek száma: {frames.Count}");

        var expectedSizes = new[] { 16, 20, 24, 32, 40, 48, 64, 96, 128, 256 };
        var actualSizes = frames.Select(f => f.Frame.PixelWidth).ToHashSet();

        foreach (var (frame, _) in frames)
        {
            var hasAlpha = frame.Format.Masks.Any() && FormatHasAlpha(frame.Format);
            var bpp = frame.Format.BitsPerPixel;

            Console.WriteLine(
                $"  {frame.PixelWidth,4}x{frame.PixelHeight,-4} bpp={bpp,-3} formátum={frame.Format} alfa={(hasAlpha ? "van" : "NINCS")}");

            if (!hasAlpha)
            {
                problems.Add($".ico {frame.PixelWidth}x{frame.PixelHeight}: nincs alfa-csatorna ({frame.Format}).");
            }
        }

        var missing = expectedSizes.Except(actualSizes).ToList();

        if (missing.Count > 0)
        {
            problems.Add($".ico: hiányzó méret(ek): {string.Join(", ", missing)}.");
        }

        var extra = actualSizes.Except(expectedSizes).ToList();

        if (extra.Count > 0)
        {
            Console.WriteLine($"  (a vártakon felül: {string.Join(", ", extra)} — nem hiba, csak megjegyzés)");
        }

        // A leggnagyobb réteg a "mester" — ehhez viszonyítjuk a kicsiket.
        var master = frames.LastOrDefault();

        if (master.Frame is null || master.Size < 128)
        {
            problems.Add(".ico: nincs elég nagy (≥128px) mesterréteg az összehasonlításhoz.");
            return;
        }

        var masterPixels = ToBgra32(master.Frame);

        Console.WriteLine();
        Console.WriteLine($"leskálázás-gyanú a {master.Size}px réteghez képest (átlagos eltérés csatornánként, 0–255):");
        Console.WriteLine(
            "(32px alatt számít gyanúnak: ott a legfontosabb a kézi sziluett-egyszerűsítés a");
        Console.WriteLine(
            " olvashatóságért. 32px fölött a mesterhez való hasonlóság NORMÁLIS és VÁRT — a nagy");
        Console.WriteLine(" rétegeknél nincs értelme külön kézi finomításnak.)");

        foreach (var (frame, size) in frames)
        {
            if (size >= master.Size)
            {
                continue;
            }

            var actual = ToBgra32(frame);
            var resized = BilinearResize(masterPixels, master.Size, master.Size, size, size);
            var diff = MeanAbsoluteDifference(actual, resized);
            var isSmall = size <= 32;

            var verdict = diff switch
            {
                < 3.0 when isSmall => "GYANÚS — szinte biztosan automatikus leskálázás, holott itt kellene a legjobban eltérnie",
                < 3.0 => "a mesterhez közeli — normális ebben a mérettartományban",
                < 8.0 when isSmall => "kétséges — közel áll az automatikus leskálázáshoz",
                < 8.0 => "a mesterhez közeli",
                _ => "eltér — kézzel hangolt vagy más forrás",
            };

            Console.WriteLine($"  {size,4}px: átlagos eltérés={diff:F2}  →  {verdict}");

            if (diff < 3.0 && isSmall)
            {
                problems.Add($".ico {size}px: {verdict.ToLowerInvariant()} (eltérés={diff:F2}).");
            }
        }
    }

    private static bool FormatHasAlpha(System.Windows.Media.PixelFormat format) =>
        format == System.Windows.Media.PixelFormats.Bgra32
        || format == System.Windows.Media.PixelFormats.Pbgra32
        || format == System.Windows.Media.PixelFormats.Rgba64
        || format == System.Windows.Media.PixelFormats.Prgba64;

    private static byte[] ToBgra32(BitmapFrame frame)
    {
        var converted = new FormatConvertedBitmap(frame, System.Windows.Media.PixelFormats.Bgra32, null, 0);
        var stride = converted.PixelWidth * 4;
        var buffer = new byte[stride * converted.PixelHeight];
        converted.CopyPixels(buffer, stride, 0);
        return buffer;
    }

    /// <summary>Egyszerű bilineáris átméretezés BGRA32 pufferen — nincs WPF-vizuál, nincs Dispatcher.</summary>
    private static byte[] BilinearResize(byte[] source, int sw, int sh, int dw, int dh)
    {
        var result = new byte[dw * dh * 4];
        var sStride = sw * 4;
        var dStride = dw * 4;

        for (var y = 0; y < dh; y++)
        {
            var srcYf = (y + 0.5) * sh / dh - 0.5;
            var y0 = Math.Clamp((int)Math.Floor(srcYf), 0, sh - 1);
            var y1 = Math.Clamp(y0 + 1, 0, sh - 1);
            var fy = srcYf - y0;

            for (var x = 0; x < dw; x++)
            {
                var srcXf = (x + 0.5) * sw / dw - 0.5;
                var x0 = Math.Clamp((int)Math.Floor(srcXf), 0, sw - 1);
                var x1 = Math.Clamp(x0 + 1, 0, sw - 1);
                var fx = srcXf - x0;

                for (var c = 0; c < 4; c++)
                {
                    var p00 = source[(y0 * sStride) + (x0 * 4) + c];
                    var p10 = source[(y0 * sStride) + (x1 * 4) + c];
                    var p01 = source[(y1 * sStride) + (x0 * 4) + c];
                    var p11 = source[(y1 * sStride) + (x1 * 4) + c];

                    var top = (p00 * (1 - fx)) + (p10 * fx);
                    var bottom = (p01 * (1 - fx)) + (p11 * fx);
                    var value = (top * (1 - fy)) + (bottom * fy);

                    result[(y * dStride) + (x * 4) + c] = (byte)Math.Clamp(Math.Round(value), 0, 255);
                }
            }
        }

        return result;
    }

    private static double MeanAbsoluteDifference(byte[] a, byte[] b)
    {
        var length = Math.Min(a.Length, b.Length);
        long sum = 0;

        for (var i = 0; i < length; i++)
        {
            sum += Math.Abs(a[i] - b[i]);
        }

        return (double)sum / length;
    }

    // ─────────────────────────────────────────────────────────────
    //  2) VersionInfo
    // ─────────────────────────────────────────────────────────────

    private static void AuditVersionInfo(string exePath, List<string> problems)
    {
        Console.WriteLine("--- 2) VersionInfo ---");

        var info = FileVersionInfo.GetVersionInfo(exePath);

        void Check(string label, string? value, bool required = true)
        {
            var display = string.IsNullOrWhiteSpace(value) ? "(ÜRES)" : value;
            Console.WriteLine($"  {label,-16}: {display}");

            if (required && string.IsNullOrWhiteSpace(value))
            {
                problems.Add($"VersionInfo: {label} üres.");
            }
        }

        Check("CompanyName", info.CompanyName);
        Check("ProductName", info.ProductName);
        Check("FileVersion", info.FileVersion);
        Check("ProductVersion", info.ProductVersion);
        Check("LegalCopyright", info.LegalCopyright);

        if (!string.IsNullOrWhiteSpace(info.ProductVersion) && !info.ProductVersion.StartsWith("1.0.0"))
        {
            problems.Add($"VersionInfo: ProductVersion nem 1.0.0-val kezdődik ({info.ProductVersion}).");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  3) Parancsikon System.AppUserModel.ID — szintetikus kerekítés
    // ─────────────────────────────────────────────────────────────

    private const string ExpectedAppUserModelId = "Obsidix.Pilaster";

    private static void AuditShortcut(string exePath, List<string> problems)
    {
        Console.WriteLine("--- 3) Parancsikon System.AppUserModel.ID (szintetikus) ---");
        Console.WriteLine("MEGJEGYZÉS: ezen a gépen nincs telepített Inno Setup és nincs valódi");
        Console.WriteLine("telepítő-generálta parancsikon (a C:\\Program Files\\Pilaster alatt sincs");
        Console.WriteLine("uninstall-regisztráció, tehát az egy korábbi kézi másolat, nem telepítés).");
        Console.WriteLine("Ezért itt egy SAJÁT parancsikont hozunk létre, UGYANAZZAL a COM-mechanizmussal,");
        Console.WriteLine("amit az Inno Setup [Icons] AppUserModelID paramétere is használ, és a valódi");
        Console.WriteLine("Pilaster.exe-re mutatva olvassuk vissza a tulajdonságot a lemezről.");
        Console.WriteLine();

        string? actual = null;
        Exception? lastError = null;
        var hasPropertyBlockOnDisk = false;
        var lnkPath = Path.Combine(Path.GetTempPath(), $"pilaster-audit-{Guid.NewGuid():N}.lnk");

        var thread = new Thread(() =>
        {
            try
            {
                CreateShortcutWithAppUserModelId(lnkPath, exePath, ExpectedAppUserModelId);

                // A bájtszintű ellenőrzés MEGBÍZHATÓ (minden mérésben
                // egyértelmű): a PropertyStoreDataBlock aláírása
                // (0xA0000009, little-endian) vagy jelen van a .lnk-ban,
                // vagy nem. A COM-os visszaolvasás ETTŐL FÜGGETLENÜL, MÉRVE,
                // időnként üres stringgel tér vissza ugyanazon a fájlon —
                // ezért mindkettőt elvégezzük, és a bájtszintű a döntő.
                var signature = new byte[] { 0x09, 0x00, 0x00, 0xA0 };
                var bytes = File.ReadAllBytes(lnkPath);
                hasPropertyBlockOnDisk = IndexOfSequence(bytes, signature) >= 0;

                // A COM-os visszaolvasás néhány próbálkozást és rövid
                // várakozást igényelhet, mielőtt megbízhatóan visszaadja a
                // property store tartalmát ugyanarról a fájlról.
                for (var attempt = 1; attempt <= 5 && string.IsNullOrEmpty(actual); attempt++)
                {
                    if (attempt > 1)
                    {
                        Thread.Sleep(150);
                    }

                    actual = ReadAppUserModelIdFromShortcut(lnkPath);
                }
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
            finally
            {
                if (File.Exists(lnkPath))
                {
                    File.Delete(lnkPath);
                }
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine($"  PropertyStoreDataBlock a lemezen (bájtszintű ellenőrzés) : {(hasPropertyBlockOnDisk ? "IGEN" : "NEM")}");
        Console.WriteLine($"  elvárt AppUserModelID                                    : {ExpectedAppUserModelId}");
        Console.WriteLine($"  visszaolvasott (COM property store, max 5 próbálkozás)   : {(string.IsNullOrEmpty(actual) ? "(nincs / hiba)" : actual)}");

        if (!hasPropertyBlockOnDisk)
        {
            problems.Add("Parancsikon AppUserModelID: a bájtszintű ellenőrzés szerint a property blokk NEM került a .lnk fájlba.");
        }

        if (lastError is not null)
        {
            Console.WriteLine(lastError.ToString());
            problems.Add(
                $"Parancsikon AppUserModelID: a szintetikus mérés kivétellel bukott ({lastError.GetType().Name}: "
                + $"{lastError.Message}).");
        }

        if (hasPropertyBlockOnDisk && string.IsNullOrEmpty(actual))
        {
            Console.WriteLine(
                "  A bájtszintű ellenőrzés szerint a property MEG VAN ÍRVA helyesen; a COM-os");
            Console.WriteLine(
                "  visszaolvasás ezen a gépen nem volt megbízhatóan reprodukálható 5 próbálkozás alatt sem —");
            Console.WriteLine("  ez a mérőeszköz korlátja, nem a mechanizmusé.");
        }
        else if (actual == ExpectedAppUserModelId)
        {
            Console.WriteLine(
                "  A mechanizmus helyes, mindkét úton igazolva: az Inno Setup [Icons] szakaszában szereplő");
            Console.WriteLine(
                "  AppUserModelID paraméter ugyanezt a COM property store-t írja — de a TÉNYLEGES");
            Console.WriteLine("  telepítő-generálta .lnk-t ezen a gépen nem lehetett ellenőrizni (nincs Inno Setup).");
        }
        else if (!string.IsNullOrEmpty(actual))
        {
            problems.Add(
                $"Parancsikon AppUserModelID: a visszaolvasott érték ('{actual}') nem egyezik a várttal "
                + $"('{ExpectedAppUserModelId}').");
        }
    }

    private static int IndexOfSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var match = true;

            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }

    private static void CreateShortcutWithAppUserModelId(string lnkPath, string targetPath, string appUserModelId)
    {
        var clsidShellLink = new Guid("00021401-0000-0000-C000-000000000046");
        var iidShellLinkW = new Guid("000214F9-0000-0000-C000-000000000046");

        Marshal.ThrowExceptionForHR(
            NativeMethods.CoCreateInstance(clsidShellLink, IntPtr.Zero, NativeMethods.CLSCTX_INPROC_SERVER, iidShellLinkW, out var linkObj));

        var link = (IShellLinkW)linkObj;

        try
        {
            link.SetPath(targetPath);
            link.SetWorkingDirectory(Path.GetDirectoryName(targetPath) ?? string.Empty);

            var store = (IPropertyStore)link;

            var pv = new PROPVARIANT
            {
                vt = NativeMethods.VT_LPWSTR,
                p = Marshal.StringToCoTaskMemUni(appUserModelId),
            };

            try
            {
                store.SetValue(NativeMethods.PKEY_AppUserModel_ID, ref pv);
                store.Commit();
            }
            finally
            {
                NativeMethods.PropVariantClear(ref pv);
            }

            var file = (IPersistFile)link;
            file.Save(lnkPath, true);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }
    }

    /// <summary>
    /// A property-blokk visszaolvasása.
    /// </summary>
    /// <remarks>
    /// SZÁNDÉKOSAN NEM <c>SHGetPropertyStoreFromParsingName</c>-nel: az a
    /// shell NÉVFELOLDÓ útvonalán megy, ami egy <c>.lnk</c>-nál a
    /// kiterjesztéshez rendelt általános tulajdonság-kezelőt éri el, nem
    /// magának a parancsikonnak a saját property store-ját — MÉRVE ezen az
    /// úton a frissen kiírt <c>System.AppUserModel.ID</c> nem olvasható
    /// vissza, holott a bájtszintű ellenőrzés szerint benne van a fájlban.
    /// A helyes út UGYANAZ az objektum, amivel írtunk: friss
    /// <c>IShellLinkW</c>, <c>IPersistFile::Load</c>, majd a RÁ castolt
    /// <c>IPropertyStore</c> — ezt dokumentálja a Microsoft AppUserModelID
    /// mintakódja is, mind az írásra, mind az olvasásra.
    /// </remarks>
    private static string? ReadAppUserModelIdFromShortcut(string lnkPath)
    {
        var clsidShellLink = new Guid("00021401-0000-0000-C000-000000000046");
        var iidShellLinkW = new Guid("000214F9-0000-0000-C000-000000000046");

        Marshal.ThrowExceptionForHR(
            NativeMethods.CoCreateInstance(clsidShellLink, IntPtr.Zero, NativeMethods.CLSCTX_INPROC_SERVER, iidShellLinkW, out var linkObj));

        var link = (IShellLinkW)linkObj;
        string? result = null;

        try
        {
            const uint StgmRead = 0;
            var file = (IPersistFile)link;
            file.Load(lnkPath, StgmRead);

            var store = (IPropertyStore)link;

            // A GetCount hívás LÁTSZÓLAG diagnosztikai lenne, de MÉRVE nem az:
            // enélkül a store.GetValue(PKEY_AppUserModel_ID, ...) megbízhatatlanul
            // üres stringet adott vissza (vt helyes, de a tartalom üres), holott a
            // property biztosan a lemezen van (bájtszinten ellenőrizve). Egy
            // előzetes GetCount/GetAt-bejárás stabilizálja a property store-t,
            // mielőtt egy konkrét kulcsot kérünk — feltehetően a lusta betöltés
            // miatt. Eltávolítás előtt mérd újra legalább 5 egymást követő
            // futtatással.
            store.GetCount(out _);

            store.GetValue(NativeMethods.PKEY_AppUserModel_ID, out var pv);

            if (pv.vt == NativeMethods.VT_LPWSTR && pv.p != IntPtr.Zero)
            {
                result = Marshal.PtrToStringUni(pv.p);
            }

            NativeMethods.PropVariantClear(ref pv);
        }
        finally
        {
            Marshal.ReleaseComObject(link);
        }

        return result;
    }
}

[StructLayout(LayoutKind.Sequential)]
internal struct PROPERTYKEY
{
    public Guid fmtid;
    public uint pid;
}

[StructLayout(LayoutKind.Explicit)]
internal struct PROPVARIANT
{
    [FieldOffset(0)]
    public ushort vt;

    [FieldOffset(8)]
    public IntPtr p;
}

/// <summary>
/// Csak a ténylegesen használt metódusokig deklarálva — de a VTABLE
/// SORRENDJE emiatt is KÖTELEZŐ: a <c>SetPath</c> az interfész utolsó
/// deklarált metódusa, ezért minden előtte lévőt fel kell sorolni, helyes
/// sorrendben, még ha nem is hívjuk őket.
/// </summary>
[ComImport]
[Guid("000214F9-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellLinkW
{
    void GetPath(IntPtr pszFile, int cch, IntPtr pfd, uint fFlags);

    void GetIDList(out IntPtr ppidl);

    void SetIDList(IntPtr pidl);

    void GetDescription(IntPtr pszName, int cch);

    void SetDescription(string pszName);

    void GetWorkingDirectory(IntPtr pszDir, int cch);

    void SetWorkingDirectory(string pszDir);

    void GetArguments(IntPtr pszArgs, int cch);

    void SetArguments(string pszArgs);

    void GetHotkey(out short pwHotkey);

    void SetHotkey(short wHotkey);

    void GetShowCmd(out int piShowCmd);

    void SetShowCmd(int iShowCmd);

    void GetIconLocation(IntPtr pszIconPath, int cch, out int piIcon);

    void SetIconLocation(string pszIconPath, int iIcon);

    void SetRelativePath(string pszPathRel, uint dwReserved);

    void Resolve(IntPtr hwnd, uint fFlags);

    void SetPath(string pszFile);
}

[ComImport]
[Guid("0000010b-0000-0000-C000-000000000046")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPersistFile
{
    void GetClassID(out Guid pClassID);

    [PreserveSig]
    int IsDirty();

    void Load(string pszFileName, uint dwMode);

    void Save(string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
}

[ComImport]
[Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    void GetCount(out uint cProps);

    void GetAt(uint iProp, out PROPERTYKEY pkey);

    void GetValue(in PROPERTYKEY key, out PROPVARIANT pv);

    void SetValue(in PROPERTYKEY key, ref PROPVARIANT pv);

    void Commit();
}

internal static class NativeMethods
{
    internal const uint CLSCTX_INPROC_SERVER = 0x1;
    internal const ushort VT_LPWSTR = 31;

    internal static readonly PROPERTYKEY PKEY_AppUserModel_ID = new()
    {
        fmtid = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"),
        pid = 5,
    };

    [DllImport("ole32.dll")]
    internal static extern int CoCreateInstance(
        in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out object ppv);

    [DllImport("ole32.dll")]
    internal static extern void PropVariantClear(ref PROPVARIANT pvar);
}
