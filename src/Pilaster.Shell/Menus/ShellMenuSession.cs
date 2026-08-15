using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;

namespace Pilaster.Shell.Menus;

/// <summary>
/// Egy megnyitott shell-menü munkamenete: a beolvasott elemfa, és a hozzá
/// tartozó, még ÉLŐ <c>IContextMenu</c>, amivel a kiválasztott parancs
/// végrehajtható.
/// </summary>
/// <remarks>
/// A COM-példány a saját STA szálán él, és csak a <see cref="Dispose"/>-ig
/// marad meg — a hívónak tehát a menü bezárásakor el KELL dobnia a
/// munkamenetet, különben a bővítmény objektumai bent ragadnának.
/// </remarks>
public sealed class ShellMenuSession : IDisposable
{
    /// <summary>Az első parancsazonosító. A shell ehhez képest ad ki azonosítókat, az invoke ezt vonja le.</summary>
    private const uint FirstCommandId = 1;

    private const uint LastCommandId = 0x7FFF;

    /// <summary>A közös STA szálat védő zár — lásd <see cref="RentShared"/>.</summary>
    private static readonly object SharedGate = new();

    private static StaWorker? _sharedWorker;

    private readonly StaWorker _worker;
    private readonly List<IDisposable> _keepAlive = [];
    private IContextMenu? _contextMenu;
    private nint _hMenu;
    private bool _disposed;

    private ShellMenuSession(StaWorker worker) => _worker = worker;

    /// <summary>
    /// A KÖZÖS STA szál. Minden lekérdezés ezen fut — nem indul új szál
    /// menünként.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Korábban minden lekérdezés saját <see cref="StaWorker"/>-t kapott, azaz
    /// minden jobbklikk új szálat indított és új COM apartmentet inicializált.
    /// Egyetlen szállal ez a költség egyszeri.
    /// </para>
    /// <para>
    /// Mellékhatásként a lekérdezések SOROSÍTVA futnak, ami megszünteti az
    /// előmelegítés és az első valódi menü versenyhelyzetét is (spec T3): a
    /// valódi lekérdezés egyszerűen az előmelegítés mögé áll a sorba, ahelyett
    /// hogy párhuzamosan ugyanazokat a DLL-eket töltenék be.
    /// </para>
    /// </remarks>
    private static StaWorker RentShared()
    {
        lock (SharedGate)
        {
            return _sharedWorker ??= new StaWorker("Pilaster.ShellMenu");
        }
    }

    /// <summary>
    /// A közös szál eldobása, ha egy bővítmény beragadt rajta.
    /// </summary>
    /// <remarks>
    /// EZ a közös szál ára, és ezért kötelező: egy megakadt bővítmény-hívást
    /// nem lehet biztonságosan félbeszakítani, tehát a szál örökre használhatatlan
    /// marad. Ha nem dobnánk el, EGYETLEN rossz bővítmény az összes további
    /// menüt megbénítaná — a saját szálas változatban ez csak egy menüt vitt el.
    /// Eldobás után a következő lekérdezés friss szálat kap.
    /// </remarks>
    private static void RetireShared(StaWorker worker)
    {
        lock (SharedGate)
        {
            if (ReferenceEquals(_sharedWorker, worker))
            {
                _sharedWorker = null;
            }
        }

        worker.Dispose();
    }

    /// <summary>A beolvasott menüelemek — már a natív menütől függetlenül.</summary>
    public IReadOnlyList<ShellMenuNode> Items { get; private set; } = [];

    /// <summary>
    /// Egy fájlkészlet shell-menüjének lekérdezése.
    /// </summary>
    /// <param name="paths">A kijelölt elemek teljes útvonalai.</param>
    /// <param name="extendedVerbs">Igaz, ha a Shift le van nyomva (bővített parancsok).</param>
    /// <param name="timeout">Ennyit várunk a bővítményekre; utána üres eredmény jön.</param>
    /// <param name="blacklist">Kikapcsolt bővítmények neve vagy CLSID-je — az illeszkedő elemek kimaradnak.</param>
    /// <returns>A munkamenet, vagy <c>null</c>, ha a lekérdezés nem sikerült vagy időtúllépés történt.</returns>
    public static Task<ShellMenuSession?> QueryItemsAsync(
        IReadOnlyList<string> paths,
        bool extendedVerbs,
        TimeSpan timeout,
        IReadOnlyCollection<string> blacklist) =>
        QueryCoreAsync(worker => CreateForItems(worker, paths), extendedVerbs, timeout, blacklist);

    /// <summary>Egy mappa HÁTTÉR-menüjének lekérdezése (üres területre kattintva).</summary>
    public static Task<ShellMenuSession?> QueryBackgroundAsync(
        string folderPath,
        bool extendedVerbs,
        TimeSpan timeout,
        IReadOnlyCollection<string> blacklist) =>
        QueryCoreAsync(worker => CreateForBackground(worker, folderPath), extendedVerbs, timeout, blacklist);

    private static async Task<ShellMenuSession?> QueryCoreAsync(
        Func<StaWorker, ShellMenuSession?> factory,
        bool extendedVerbs,
        TimeSpan timeout,
        IReadOnlyCollection<string> blacklist)
    {
        var worker = RentShared();

        var build = worker.RunAsync(() =>
        {
            var session = factory(worker);
            session?.Populate(extendedVerbs, blacklist);
            return session;
        });

        // Időkorlát: a saját menüelemek AZONNAL megjelennek, a shell-elemek
        // pedig csak akkor csúsznak be, ha időben megérkeznek (spec F4).
        var finished = await Task.WhenAny(build, Task.Delay(timeout)).ConfigureAwait(false);

        if (finished != build)
        {
            // Nem szakítjuk félbe a szálat — egy beragadt bővítményt nem lehet
            // biztonságosan megszakítani. A KÖZÖS szálat viszont el kell dobni:
            // ami rajta ragadt, az minden további menüt blokkolna.
            RetireShared(worker);

            _ = build.ContinueWith(
                t => t.Result?.Dispose(),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnRanToCompletion,
                TaskScheduler.Default);

            return null;
        }

        try
        {
            // A közös szálat NEM dobjuk el sikeres vagy eredménytelen
            // lekérdezés után — épp az a lényege, hogy megmarad.
            return await build.ConfigureAwait(false);
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// A shell COM-gépezetének előmelegítése.
    /// </summary>
    /// <remarks>
    /// <para>
    /// MÉRVE: az első lekérdezés lényegesen tovább tart, mint a többi, mert
    /// ilyenkor indul a COM apartment és töltődnek be a bővítmény-DLL-ek. Ez
    /// EGYSZERI költség, nem menünkénti — ha induláskor, háttérben elvégezzük,
    /// az első valódi jobbklikk már a gyors úton megy.
    /// </para>
    /// <para>
    /// Alacsony prioritású háttérszálon fut, és minden hibát elnyel: egy
    /// sikertelen előmelegítés semmit nem ront el, csak az első menü lesz
    /// lassabb.
    /// </para>
    /// <para>
    /// A tényleges COM-munka a KÖZÖS STA szálon fut (lásd
    /// <see cref="RentShared"/>), nem itt — ez a szál csak megvárja. Ezért nem
    /// versenyezhet az első valódi jobbklikkel: az egyszerűen mögé áll a sorba
    /// (spec T3).
    /// </para>
    /// </remarks>
    /// <param name="markInflight">
    /// Jelzi, hogy shell-hívás van folyamatban (útvonal, fajta). Az
    /// előmelegítés UGYANÚGY betölti a bővítményeket, mint egy valódi menü,
    /// tehát ugyanúgy el is tudja vinni a folyamatot — e nélkül egy
    /// előmelegítés közbeni összeomlás MINDEN indulásnál megismétlődne, és a
    /// program elindíthatatlan lenne.
    /// </param>
    /// <param name="clearInflight">A jelző törlése a sikeres befejezés után.</param>
    public static void WarmUp(Action<string, string>? markInflight = null, Action? clearInflight = null)
    {
        var thread = new Thread(() =>
        {
            try
            {
                var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

                // MAPPA-menü: a Directory/Background kezelőket tölti be.
                markInflight?.Invoke(profile, "background");

                QueryBackgroundAsync(profile, false, TimeSpan.FromSeconds(20), [])
                    .GetAwaiter().GetResult()?.Dispose();

                // FÁJL-menü: MÉRVE, ez KÜLÖN DLL-készlet (tömörítők,
                // víruskeresők, szerkesztők). A mappa-menü előmelegítése
                // önmagában alig gyorsított a fájlokon — a kettőt együtt kell
                // melegíteni. Célnak a saját futtatható fájlunk a
                // legbiztonságosabb: mindig létezik, és a rá vonatkozó
                // kezelők ugyanazok, mint bármely más fájlnál.
                if (Environment.ProcessPath is { Length: > 0 } self)
                {
                    markInflight?.Invoke(self, "items");

                    QueryItemsAsync([self], false, TimeSpan.FromSeconds(20), [])
                        .GetAwaiter().GetResult()?.Dispose();
                }
            }
            catch (Exception)
            {
                // Az előmelegítés hibája sosem érdekes.
            }
            finally
            {
                // Az előmelegítés induláskor, a felhasználó első jobbklikkje
                // előtt fut le, ezért nem törölhet el egy valódi menü jelzőjét.
                clearInflight?.Invoke();
            }
        })
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "Pilaster.ShellWarmUp",
        };

        thread.Start();
    }

    private static ShellMenuSession? CreateForItems(StaWorker worker, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return null;
        }

        var session = new ShellMenuSession(worker);
        var items = new ShellItem[paths.Count];

        try
        {
            for (var i = 0; i < paths.Count; i++)
            {
                items[i] = ShellItem.Open(paths[i]);
            }

            var menu = ShellContextMenu.CreateFromItems(items, out var keepAlive);

            // A SORREND KRITIKUS. A lista fordítva ürül (lásd Dispose), tehát
            // az UTOLJÁRA hozzáadott elem szabadul fel ELŐSZÖR. A Vanara
            // dokumentációja szerint a `keepAlive`-nak TÚL kell élnie a
            // ShellContextMenu-t, ezért a menü kerül a lista végére.
            //
            // Fordított sorrendben ez heap-korrupcióval (0xC0000374) omlasztotta
            // el a programot: MÉRVE 6 gyors lekérdezés-sorozatból 3 esetben.
            session._keepAlive.Add(keepAlive);
            session._keepAlive.Add(menu);
            session._contextMenu = menu.ComInterface;

            return session;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or Win32Exception or FileNotFoundException)
        {
            session.Dispose();
            return null;
        }
    }

    private static ShellMenuSession? CreateForBackground(StaWorker worker, string folderPath)
    {
        var session = new ShellMenuSession(worker);

        try
        {
            SHCreateItemHandlerFromParsingName(folderPath, out IShellFolder? folder, BHID.BHID_SFObject).ThrowIfFailed();

            if (folder is null)
            {
                return null;
            }

            // IShellFolder::CreateViewObject(hwnd, IID_IContextMenu) adja magának
            // a MAPPÁNAK a háttér-menüjét — ugyanaz, mint az Intézőben üres
            // területre kattintva.
            var contextMenu = folder.CreateViewObject<IContextMenu>(HWND.NULL);

            if (contextMenu is null)
            {
                Marshal.ReleaseComObject(folder);
                return null;
            }

            session._contextMenu = contextMenu;
            session._keepAlive.Add(new ComRelease(folder));
            session._keepAlive.Add(new ComRelease(contextMenu));

            return session;
        }
        catch (Exception ex) when (ex is COMException or ArgumentException or InvalidOperationException or Win32Exception)
        {
            session.Dispose();
            return null;
        }
    }

    /// <summary>A rejtett <c>HMENU</c> feltöltése és a fa beolvasása.</summary>
    private void Populate(bool extendedVerbs, IReadOnlyCollection<string> blacklist)
    {
        if (_contextMenu is null)
        {
            return;
        }

        _hMenu = NativeMenuInterop.CreatePopupMenu();

        if (_hMenu == nint.Zero)
        {
            return;
        }

        // CMF_EXTENDEDVERBS: a Shift-tel lenyomott menü bővebb parancskészlete.
        var flags = CMF.CMF_NORMAL | (extendedVerbs ? CMF.CMF_EXTENDEDVERBS : 0);

        try
        {
            _contextMenu.QueryContextMenu(_hMenu, 0, FirstCommandId, LastCommandId, flags);
        }
        catch (Exception ex) when (ex is COMException or Win32Exception)
        {
            return;
        }

        Items = ReadMenu(_hMenu, blacklist, depth: 0);
    }

    /// <summary>
    /// A menüfa rekurzív beolvasása.
    /// </summary>
    /// <remarks>
    /// A mélységkorlát nem elméleti óvatosság: egy hibás bővítmény önmagára
    /// mutató almenüt is visszaadhat, és abból végtelen rekurzió lenne.
    /// </remarks>
    private List<ShellMenuNode> ReadMenu(nint hMenu, IReadOnlyCollection<string> blacklist, int depth)
    {
        var nodes = new List<ShellMenuNode>();

        if (depth > 5)
        {
            return nodes;
        }

        var count = NativeMenuInterop.GetMenuItemCount(hMenu);

        for (var index = 0; index < count; index++)
        {
            var buffer = Marshal.AllocHGlobal(512 * sizeof(char));

            try
            {
                var info = new NativeMenuInterop.MENUITEMINFO
                {
                    cbSize = (uint)Marshal.SizeOf<NativeMenuInterop.MENUITEMINFO>(),
                    fMask = NativeMenuInterop.MIIM_STRING | NativeMenuInterop.MIIM_SUBMENU
                        | NativeMenuInterop.MIIM_STATE | NativeMenuInterop.MIIM_ID
                        | NativeMenuInterop.MIIM_FTYPE | NativeMenuInterop.MIIM_BITMAP,
                    dwTypeData = buffer,
                    cch = 512,
                };

                if (!NativeMenuInterop.GetMenuItemInfo(hMenu, (uint)index, byPosition: true, ref info))
                {
                    continue;
                }

                if ((info.fType & NativeMenuInterop.MFT_SEPARATOR) != 0)
                {
                    // Egymás melletti vagy vezető elválasztók elhagyása — a
                    // saját menü rendezettsége fontosabb, mint a natív menü
                    // pontos másolása.
                    if (nodes.Count > 0 && !nodes[^1].IsSeparator)
                    {
                        nodes.Add(new ShellMenuNode { IsSeparator = true });
                    }

                    continue;
                }

                var text = (Marshal.PtrToStringUni(info.dwTypeData, (int)info.cch) ?? string.Empty)
                    .Replace("&", string.Empty)
                    .Trim();

                if (text.Length == 0 || IsBlacklisted(text, blacklist))
                {
                    continue;
                }

                var children = info.hSubMenu != nint.Zero
                    ? ReadSubmenu(info.hSubMenu, index, blacklist, depth)
                    : [];

                nodes.Add(new ShellMenuNode
                {
                    Text = text,
                    CommandId = info.hSubMenu != nint.Zero ? 0 : info.wID,
                    IsEnabled = (info.fState & NativeMenuInterop.MFS_GRAYED) == 0,
                    IsChecked = (info.fState & NativeMenuInterop.MFS_CHECKED) != 0,
                    Icon = NativeMenuInterop.TryConvertBitmap(info.hbmpItem),
                    Children = children,
                });
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        // Záró elválasztó levágása.
        while (nodes.Count > 0 && nodes[^1].IsSeparator)
        {
            nodes.RemoveAt(nodes.Count - 1);
        }

        return nodes;
    }

    /// <summary>
    /// Egy almenü beolvasása. A dinamikusan feltöltődő almenüket (7-Zip,
    /// Virtual CloneDrive) előbb „meg kell kérni", hogy töltsék fel magukat —
    /// erre való az <c>IContextMenu3::HandleMenuMsg2</c> a
    /// <c>WM_INITMENUPOPUP</c> üzenettel (spec F4/6).
    /// </summary>
    private List<ShellMenuNode> ReadSubmenu(nint hSubMenu, int index, IReadOnlyCollection<string> blacklist, int depth)
    {
        try
        {
            if (_contextMenu is IContextMenu3 menu3)
            {
                menu3.HandleMenuMsg2(NativeMenuInterop.WM_INITMENUPOPUP, hSubMenu, index, out _);
            }
            else if (_contextMenu is IContextMenu2 menu2)
            {
                menu2.HandleMenuMsg(NativeMenuInterop.WM_INITMENUPOPUP, hSubMenu, index);
            }
        }
        catch (Exception ex) when (ex is COMException or Win32Exception or InvalidCastException or NotImplementedException)
        {
            // A bővítmény nem támogatja a dinamikus feltöltést — ilyenkor az
            // almenü már statikusan kész, olvasható tovább.
        }

        return ReadMenu(hSubMenu, blacklist, depth + 1);
    }

    private static bool IsBlacklisted(string text, IReadOnlyCollection<string> blacklist) =>
        blacklist.Count > 0
        && blacklist.Any(entry => text.Contains(entry, StringComparison.CurrentCultureIgnoreCase));

    /// <summary>
    /// Egy menüparancs végrehajtása. Ugyanazon az STA szálon fut, ahol a
    /// menü létrejött — a shell-bővítmények apartment-kötöttek.
    /// </summary>
    /// <param name="commandId">A <see cref="ShellMenuNode.CommandId"/> értéke.</param>
    /// <param name="ownerWindowHandle">A shell párbeszédeinek (pl. törlés megerősítése) szülőablaka.</param>
    public Task<bool> InvokeAsync(uint commandId, nint ownerWindowHandle)
    {
        if (_disposed || _contextMenu is null || commandId < FirstCommandId)
        {
            return Task.FromResult(false);
        }

        return _worker.RunAsync(() =>
        {
            try
            {
                // A verb a NULLÁTÓL indexelt parancssorszám — a menü
                // azonosítójából le kell vonni a kiinduló eltolást. A Vanara
                // ResourceId-je végzi el a MAKEINTRESOURCE-átalakítást, és
                // tölti ki az unicode (lpVerbW) párját is.
                var offset = (int)(commandId - FirstCommandId);

                var invoke = new CMINVOKECOMMANDINFOEX(
                    offset,
                    ShowWindowCommand.SW_SHOWNORMAL,
                    (HWND)ownerWindowHandle);

                _contextMenu.InvokeCommand(invoke);
                return true;
            }
            catch (Exception ex) when (ex is COMException or Win32Exception or ArgumentException or InvalidOperationException)
            {
                // Egy hibás bővítmény parancsa nem dobhatja el az appot.
                return false;
            }
        });
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // A takarítás is az STA szálon kell hogy fusson: a COM-példányokat
        // azon a szálon kell elengedni, amelyiken létrejöttek.
        _ = _worker.RunAsync(() =>
        {
            if (_hMenu != nint.Zero)
            {
                NativeMenuInterop.DestroyMenu(_hMenu);
                _hMenu = nint.Zero;
            }

            _contextMenu = null;

            for (var i = _keepAlive.Count - 1; i >= 0; i--)
            {
                try
                {
                    _keepAlive[i].Dispose();
                }
                catch (Exception)
                {
                    // Egy bővítmény hibás Release-e nem akadályozhatja meg a
                    // többi elengedését.
                }
            }

            _keepAlive.Clear();
            return true;
        });

        // A szálat NEM zárjuk le: közös, és a következő menü is ezt használja.
    }

    /// <summary>Egy nyers COM-mutató elengedése <see cref="IDisposable"/> alakban, hogy a keep-alive lista egységes lehessen.</summary>
    private sealed class ComRelease(object comObject) : IDisposable
    {
        public void Dispose()
        {
            if (Marshal.IsComObject(comObject))
            {
                Marshal.ReleaseComObject(comObject);
            }
        }
    }
}
