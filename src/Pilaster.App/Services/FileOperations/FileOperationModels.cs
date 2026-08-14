using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Pilaster.Core.Formatting;

namespace Pilaster.App.Services.FileOperations;

public enum FileOperationKind
{
    Copy,
    Move,
    Delete,
}

public enum FileOperationState
{
    Running,
    Paused,
    Completed,
    Cancelled,

    /// <summary>Legalább egy fájl hibára futott, de a művelet a többivel folytatódott/befejeződött.</summary>
    CompletedWithErrors,
}

public enum FileConflictAction
{
    Overwrite,
    Skip,
    KeepBoth,
}

/// <summary>Egy megoldandó névütközés — a felhasználó dönt, hogyan folytatódjon a művelet.</summary>
public sealed partial class FileConflictPrompt : ObservableObject
{
    public required string SourcePath { get; init; }

    public required string DestinationPath { get; init; }

    public required long SourceSize { get; init; }

    public required long DestinationSize { get; init; }

    public required DateTime SourceModifiedUtc { get; init; }

    public required DateTime DestinationModifiedUtc { get; init; }

    public string SourceSizeText => ByteSize.Format(SourceSize);

    public string DestinationSizeText => ByteSize.Format(DestinationSize);

    [ObservableProperty]
    public partial bool ApplyToAll { get; set; }

    internal TaskCompletionSource<FileConflictAction> Result { get; } = new();

    [RelayCommand]
    private void Resolve(FileConflictAction action) => Result.TrySetResult(action);
}

/// <summary>
/// Egy futó (vagy befejezett) másolási/áthelyezési/törlési művelet állapota —
/// ez a típus közvetlenül köthető az Aktivitás-központ paneljéhez.
/// </summary>
public sealed partial class FileOperationJob : ObservableObject
{
    public required Guid Id { get; init; }

    public required FileOperationKind Kind { get; init; }

    public required string DestinationDirectory { get; init; }

    public required int TotalFiles { get; init; }

    [ObservableProperty]
    public partial FileOperationState State { get; set; } = FileOperationState.Running;

    [ObservableProperty]
    public partial string CurrentFileName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial int FilesCompleted { get; set; }

    [ObservableProperty]
    public partial long BytesCompleted { get; set; }

    [ObservableProperty]
    public partial long TotalBytes { get; set; }

    [ObservableProperty]
    public partial double BytesPerSecond { get; set; }

    [ObservableProperty]
    public partial string? ErrorSummary { get; set; }

    /// <summary>Nem null, amíg egy névütközésre vár a felhasználó válaszára.</summary>
    [ObservableProperty]
    public partial FileConflictPrompt? PendingConflict { get; set; }

    public bool IsActive => State is FileOperationState.Running or FileOperationState.Paused;

    public bool IsPaused => State == FileOperationState.Paused;

    public double ProgressFraction => TotalBytes <= 0 ? 0 : Math.Clamp((double)BytesCompleted / TotalBytes, 0, 1);

    public string ProgressText => $"{FilesCompleted}/{TotalFiles} · {ByteSize.Format(BytesCompleted)} / {ByteSize.Format(TotalBytes)}";

    public string SpeedText => IsActive && BytesPerSecond > 0 ? $"{ByteSize.Format((long)BytesPerSecond)}/s" : string.Empty;

    public string? EtaText
    {
        get
        {
            if (!IsActive || BytesPerSecond <= 0 || TotalBytes <= 0)
            {
                return null;
            }

            var remaining = TotalBytes - BytesCompleted;
            var seconds = remaining / BytesPerSecond;
            return seconds switch
            {
                < 60 => $"{(int)seconds} mp",
                < 3600 => $"{(int)(seconds / 60)} p",
                _ => $"{(int)(seconds / 3600)} ó",
            };
        }
    }

    /// <summary>
    /// A jelenleg írt célfájl útvonala — megszakításkor ez alapján törli az
    /// engine a félbemaradt, hiányos célfájlt. Csak az engine írja.
    /// </summary>
    internal string? CurrentDestinationPath { get; set; }

    internal CancellationTokenSource Cancellation { get; } = new();

    internal AsyncPauseGate PauseGate { get; } = new();

    partial void OnStateChanged(FileOperationState value)
    {
        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsPaused));
        PauseCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnBytesCompletedChanged(long value)
    {
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnTotalBytesChanged(long value)
    {
        OnPropertyChanged(nameof(ProgressFraction));
        OnPropertyChanged(nameof(ProgressText));
    }

    partial void OnFilesCompletedChanged(int value) => OnPropertyChanged(nameof(ProgressText));

    partial void OnBytesPerSecondChanged(double value)
    {
        OnPropertyChanged(nameof(SpeedText));
        OnPropertyChanged(nameof(EtaText));
    }

    /// <summary>
    /// A <c>CanExecute</c>-ok csak a UI-ból (gomb <c>IsEnabled</c>) induló
    /// hívásokat védik — programozott <c>Execute()</c> hívás megkerülheti
    /// őket. Emiatt a metódusok TESTÉBEN is ellenőrzünk: egy elkésett
    /// Szünet/Folytatás egy közben befejeződött műveleten így ártalmatlan
    /// no-op marad, nem írja felül a végállapotot (Completed/Cancelled).
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanPause))]
    private void Pause()
    {
        if (!CanPause())
        {
            return;
        }

        State = FileOperationState.Paused;
        PauseGate.Pause();
    }

    private bool CanPause() => State == FileOperationState.Running;

    [RelayCommand(CanExecute = nameof(CanResume))]
    private void Resume()
    {
        if (!CanResume())
        {
            return;
        }

        State = FileOperationState.Running;
        PauseGate.Resume();
    }

    private bool CanResume() => State == FileOperationState.Paused;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        if (!CanCancel())
        {
            return;
        }

        Cancellation.Cancel();
        PauseGate.Resume(); // ha épp szüneteltetve volt, engedjük ki a várakozásból, hogy a megszakítás azonnal lefusson.
    }

    private bool CanCancel() => IsActive;
}

/// <summary>
/// Aszinkron „kapu" a szüneteltetéshez: szünetkor a másolóhurok minden egyes
/// darab (chunk) előtt itt vár, amíg valaki nem folytatja — szemben egy
/// szinkron zárral, ez nem foglal szálat várakozás közben.
/// </summary>
internal sealed class AsyncPauseGate
{
    private TaskCompletionSource _resumeSignal = CreateSignaled();

    public bool IsPaused { get; private set; }

    public void Pause()
    {
        if (IsPaused)
        {
            return;
        }

        IsPaused = true;
        _resumeSignal = new TaskCompletionSource();
    }

    public void Resume()
    {
        if (!IsPaused)
        {
            return;
        }

        IsPaused = false;
        _resumeSignal.TrySetResult();
    }

    public Task WaitIfPausedAsync() => IsPaused ? _resumeSignal.Task : Task.CompletedTask;

    private static TaskCompletionSource CreateSignaled()
    {
        var tcs = new TaskCompletionSource();
        tcs.SetResult();
        return tcs;
    }
}
