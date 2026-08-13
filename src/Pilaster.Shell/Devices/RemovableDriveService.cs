using System.IO;
using System.Runtime.InteropServices;
using Vanara.PInvoke;

namespace Pilaster.Shell.Devices;

public enum EjectOutcome
{
    Succeeded,

    /// <summary>Egy másik folyamat nyitva tart valamit a köteten.</summary>
    InUse,

    Error,
}

public readonly record struct EjectResult(EjectOutcome Outcome);

/// <summary>
/// Cserélhető meghajtók (USB, külső HDD, optikai) biztonságos leválasztása.
/// </summary>
/// <remarks>
/// <para>
/// Szándékosan a kötet-szintű (<c>FSCTL_LOCK_VOLUME</c> /
/// <c>FSCTL_DISMOUNT_VOLUME</c> / <c>IOCTL_STORAGE_MEDIA_REMOVAL</c>) utat
/// választja a teljes PnP „Biztonságos eltávolítás" eszköztár-ikon mögötti
/// mechanizmus (<c>CM_Request_Device_Eject</c> + eszközfa-bejárás) helyett.
/// Ugyanazt az adatbiztonsági garanciát adja — zárolás, majd leválasztás,
/// majd az eltávolítás engedélyezése —, és a zárolás sikertelensége éppen a
/// „használatban van" hiba természetes forrása, lényegesen kevesebb és
/// jobban dokumentált Win32-hívással, mint az eszközpéldány-fa bejárása.
/// </para>
/// <para>
/// Optikai meghajtónál a tálca kinyitása nem igényel kötetzárolást — az
/// akkor is működjön, ha a meghajtóban épp nincs lemez, ezért az a lock
/// nélkül, közvetlenül fut.
/// </para>
/// </remarks>
public static class RemovableDriveService
{
    private const uint FsctlLockVolume = 0x00090018;
    private const uint FsctlDismountVolume = 0x00090020;
    private const uint IoctlStorageMediaRemoval = 0x002D4804;
    private const uint IoctlStorageEjectMedia = 0x002D4808;

    private const int LockRetryCount = 4;
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(250);

    /// <summary>Igaz, ha ez a meghajtótípus szoftveresen leválasztható/kiadható.</summary>
    public static bool IsEjectable(DriveType driveType) =>
        driveType is DriveType.Removable or DriveType.CDRom;

    public static EjectResult Eject(string driveLetter, DriveType driveType)
    {
        var root = driveLetter.TrimEnd('\\', '/');

        if (root.Length == 0)
        {
            return new EjectResult(EjectOutcome.Error);
        }

        using var handle = Kernel32.CreateFile(
            $@"\\.\{root}",
            Kernel32.FileAccess.GENERIC_READ | Kernel32.FileAccess.GENERIC_WRITE,
            FileShare.ReadWrite,
            null,
            FileMode.Open,
            0);

        if (handle.IsInvalid)
        {
            return new EjectResult(EjectOutcome.Error);
        }

        if (driveType == DriveType.CDRom)
        {
            Kernel32.DeviceIoControl(handle, IoctlStorageEjectMedia);
            return new EjectResult(EjectOutcome.Succeeded);
        }

        if (!TryLockVolume(handle))
        {
            return new EjectResult(EjectOutcome.InUse);
        }

        Kernel32.DeviceIoControl(handle, FsctlDismountVolume);
        Kernel32.DeviceIoControl(handle, IoctlStorageMediaRemoval, new PreventMediaRemoval());

        // Sok pendrive-nál nem támogatott IOCTL — ártalmatlanul sikertelen,
        // a kötet a leválasztás után attól még biztonságosan eltávolítható.
        Kernel32.DeviceIoControl(handle, IoctlStorageEjectMedia);

        return new EjectResult(EjectOutcome.Succeeded);
    }

    private static bool TryLockVolume(Kernel32.SafeHFILE handle)
    {
        for (var attempt = 0; attempt < LockRetryCount; attempt++)
        {
            if (Kernel32.DeviceIoControl(handle, FsctlLockVolume))
            {
                return true;
            }

            Thread.Sleep(LockRetryDelay);
        }

        return false;
    }

    /// <summary>
    /// A natív <c>PREVENT_MEDIA_REMOVAL</c> egyetlen mezője <c>BOOLEAN</c>
    /// (1 bájt), NEM <c>BOOL</c> (4 bájt) — ezért nyers <see cref="byte"/>,
    /// nem <see cref="UnmanagedType.Bool"/>-lal jelölt <see cref="bool"/>.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private readonly struct PreventMediaRemoval
    {
        public readonly byte PreventMediaRemovalFlag = 0;

        public PreventMediaRemoval()
        {
        }
    }
}
