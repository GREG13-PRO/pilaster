using System.Runtime.InteropServices;

namespace ShellCrashRepro;

/// <summary>
/// A shell context menu MINIMÁLIS reprodukciója.
/// </summary>
/// <remarks>
/// <para>
/// Ez a program azt a kérdést dönti el, hogy a Windows shell-bővítmények
/// önmagukban rontják-e a hívó folyamat heapjét (<c>0xC0000374</c>), vagy a
/// Pilaster shell-gazdálkodása a hibás. Ezért NINCS benne semmi a Pilaster
/// kódjából, se Vanara, se WPF — csak a legszükségesebb, nyers P/Invoke.
/// </para>
/// <para>
/// A menetrend körönként: <c>SHParseDisplayName</c> → <c>SHBindToParent</c> →
/// <c>GetUIObjectOf(IID_IContextMenu)</c> → <c>QueryContextMenu</c> →
/// <c>DestroyMenu</c> → <c>Release</c>. A szál STA és PUMPÁL — az STA
/// definíció szerint üzenethurkot futtató apartment, és a bővítmények erre
/// építenek.
/// </para>
/// <para>
/// Kilépési kód: 0 = minden kör lefutott. Bármi más (jellemzően a natív
/// <c>0xC0000374</c>) = a folyamat elszállt.
/// </para>
/// </remarks>
internal static class Program
{
    private static int Main(string[] args)
    {
        var target = args.Length > 0
            ? args[0]
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "notepad.exe");

        var rounds = args.Length > 1 && int.TryParse(args[1], out var parsed) ? parsed : 10;

        Console.WriteLine($"cel      : {target}");
        Console.WriteLine($"korok    : {rounds}");
        Console.WriteLine($"letezik  : {File.Exists(target) || Directory.Exists(target)}");
        Console.WriteLine();

        var completed = 0;

        var thread = new Thread(() => completed = RunRounds(target, rounds))
        {
            IsBackground = false,
            Name = "ShellCrashRepro.STA",
        };

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Console.WriteLine();
        Console.WriteLine($"befejezett korok: {completed} / {rounds}");

        return completed == rounds ? 0 : 1;
    }

    private static int RunRounds(string target, int rounds)
    {
        var completed = 0;

        for (var round = 0; round < rounds; round++)
        {
            Console.WriteLine($"kor {round}: indul");
            Console.Out.Flush();

            try
            {
                QueryOnce(target);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"kor {round}: KIVETEL {ex.GetType().Name}: {ex.Message}");
                Console.Out.Flush();
                return completed;
            }

            completed++;
            Console.WriteLine($"kor {round}: kesz");
            Console.Out.Flush();

            // Az STA apartmentnek PUMPÁLNIA kell: a bővítmények ablakot
            // hozhatnak létre a hívó szálon, és a lebontásuk üzeneteket
            // igényel. Pumpa nélkül azok felgyűlnek.
            PumpMessages(milliseconds: 400);

            // Rásegítünk arra, ami a valóságban két jobbklikk között történne.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            PumpMessages(milliseconds: 400);
        }

        return completed;
    }

    private static void QueryOnce(string target)
    {
        var hr = SHParseDisplayName(target, nint.Zero, out var pidl, 0, out _);
        Marshal.ThrowExceptionForHR(hr);

        nint folderPtr = nint.Zero;
        nint menuPtr = nint.Zero;
        var hMenu = nint.Zero;

        try
        {
            var folderIid = IID_IShellFolder;
            hr = SHBindToParent(pidl, ref folderIid, out folderPtr, out var childPidl);
            Marshal.ThrowExceptionForHR(hr);

            var folder = (IShellFolder)Marshal.GetObjectForIUnknown(folderPtr);

            try
            {
                var menuIid = IID_IContextMenu;

                hr = folder.GetUIObjectOf(nint.Zero, 1, [childPidl], ref menuIid, nint.Zero, out menuPtr);
                Marshal.ThrowExceptionForHR(hr);

                var contextMenu = (IContextMenu)Marshal.GetObjectForIUnknown(menuPtr);

                try
                {
                    hMenu = CreatePopupMenu();

                    if (hMenu == nint.Zero)
                    {
                        throw new InvalidOperationException("CreatePopupMenu sikertelen.");
                    }

                    hr = contextMenu.QueryContextMenu(hMenu, 0, 1, 0x7FFF, CMF_NORMAL);

                    if (hr < 0)
                    {
                        Marshal.ThrowExceptionForHR(hr);
                    }

                    Console.WriteLine($"   elemek: {GetMenuItemCount(hMenu)}");
                }
                finally
                {
                    Marshal.ReleaseComObject(contextMenu);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(folder);
            }
        }
        finally
        {
            if (hMenu != nint.Zero)
            {
                DestroyMenu(hMenu);
            }

            if (menuPtr != nint.Zero)
            {
                Marshal.Release(menuPtr);
            }

            if (folderPtr != nint.Zero)
            {
                Marshal.Release(folderPtr);
            }

            ILFree(pidl);
        }
    }

    /// <summary>Üzenetek kiszolgálása a megadott ideig — az STA apartment kötelessége.</summary>
    private static void PumpMessages(int milliseconds)
    {
        var deadline = Environment.TickCount64 + milliseconds;

        while (Environment.TickCount64 < deadline)
        {
            while (PeekMessage(out var message, nint.Zero, 0, 0, PM_REMOVE))
            {
                TranslateMessage(ref message);
                DispatchMessage(ref message);
            }

            Thread.Sleep(10);
        }
    }

    private const uint CMF_NORMAL = 0x00000000;
    private const uint PM_REMOVE = 0x0001;

    private static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");
    private static readonly Guid IID_IContextMenu = new("000214E4-0000-0000-C000-000000000046");

    [ComImport]
    [Guid("000214E6-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellFolder
    {
        [PreserveSig] int ParseDisplayName(nint hwnd, nint pbc, [MarshalAs(UnmanagedType.LPWStr)] string name, ref uint eaten, out nint pidl, ref uint attributes);
        [PreserveSig] int EnumObjects(nint hwnd, int flags, out nint enumIdList);
        [PreserveSig] int BindToObject(nint pidl, nint pbc, ref Guid riid, out nint ppv);
        [PreserveSig] int BindToStorage(nint pidl, nint pbc, ref Guid riid, out nint ppv);
        [PreserveSig] int CompareIDs(nint lParam, nint pidl1, nint pidl2);
        [PreserveSig] int CreateViewObject(nint hwndOwner, ref Guid riid, out nint ppv);
        [PreserveSig] int GetAttributesOf(uint cidl, [In][MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref uint inOut);
        [PreserveSig] int GetUIObjectOf(nint hwndOwner, uint cidl, [In][MarshalAs(UnmanagedType.LPArray)] nint[] apidl, ref Guid riid, nint reserved, out nint ppv);
        [PreserveSig] int GetDisplayNameOf(nint pidl, uint flags, nint name);
        [PreserveSig] int SetNameOf(nint hwnd, nint pidl, [MarshalAs(UnmanagedType.LPWStr)] string name, uint flags, out nint pidlOut);
    }

    [ComImport]
    [Guid("000214E4-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IContextMenu
    {
        [PreserveSig] int QueryContextMenu(nint hMenu, uint indexMenu, uint idCmdFirst, uint idCmdLast, uint flags);
        [PreserveSig] int InvokeCommand(nint pici);
        [PreserveSig] int GetCommandString(nint idCmd, uint type, nint reserved, nint name, uint cchMax);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public nint hwnd;
        public uint message;
        public nint wParam;
        public nint lParam;
        public uint time;
        public int ptX;
        public int ptY;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint sfgaoIn, out uint sfgaoOut);

    [DllImport("shell32.dll")]
    private static extern int SHBindToParent(nint pidl, ref Guid riid, out nint ppv, out nint pidlLast);

    [DllImport("shell32.dll")]
    private static extern void ILFree(nint pidl);

    [DllImport("user32.dll")]
    private static extern nint CreatePopupMenu();

    [DllImport("user32.dll")]
    private static extern bool DestroyMenu(nint hMenu);

    [DllImport("user32.dll")]
    private static extern int GetMenuItemCount(nint hMenu);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    private static extern bool PeekMessage(out MSG message, nint hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG message);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    private static extern nint DispatchMessage(ref MSG message);
}
