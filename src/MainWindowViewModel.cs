using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesScanner.Helpers;
using FilesScanner.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Application = System.Windows.Application;

namespace FilesScanner;

public partial class MainWindowViewModel : ObservableObject {
    readonly DiskService _diskService;
    readonly PauseTokenSource _pauseTokenSource = new();
    readonly ManualResetEventSlim _scanCancellationManualResetEventSlim = new(false);
    CancellationTokenSource _scanCancellationTokenSource;

    [ObservableProperty] string[] _drives;
    [ObservableProperty] string _selectedDrive;
    [ObservableProperty] ObservableCollection<FolderModel> _foldersModels = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PauseScanCommand))]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    ScanMode _currentScanMode;

    public MainWindowViewModel() {
        _diskService = new DiskService();
        _drives = _diskService.GetDrives();
        if (_drives is { Length: > 0 }) {
            SelectedDrive = _drives[0];
        }

        CurrentScanMode = ScanMode.CanStartScan;
    }

    bool IsScanRunning() {
        return CurrentScanMode == ScanMode.Scanning;
    }

    bool CanCancelScan() {
        return CurrentScanMode != ScanMode.CanStartScan;
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    public void CancelScan() {
        try {
            if (!_scanCancellationTokenSource.IsCancellationRequested) {
                _scanCancellationTokenSource.Cancel();
                _scanCancellationManualResetEventSlim.Wait(_scanCancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException) {
            // ignore
        }
        catch (Exception e) {
            //log error
        }
        finally {
            FoldersModels.Clear();
            CurrentScanMode = ScanMode.CanStartScan;
            _scanCancellationTokenSource.Dispose();
            _scanCancellationTokenSource = null;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public async Task StartScan() {
        //var scanTask = Task.Run(async () => {
        //    //var t = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        //    //await Parallel.ForEachAsync(_diskService.ScanDrive(SelectedDrive), t, Test);

        //    await foreach (var folderModel in _diskService.ScanDrive(SelectedDrive)) {
        //        Application.Current.Dispatcher.BeginInvoke(() => {
        //            FoldersModels.Add(folderModel);
        //        });
        //    }
        //});

        //return scanTask;

        if (IsScanRunning()) {
            PauseScan();
            return;
        }

        if (_pauseTokenSource.IsPaused) {
            _pauseTokenSource.IsPaused = false;
            CurrentScanMode = ScanMode.Scanning;
            return;
        }

        FoldersModels.Clear();
        CurrentScanMode = ScanMode.Scanning;
        _scanCancellationTokenSource = new();

        var scanTask = Task.Run(async () => {
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
            await Parallel.ForEachAsync(_diskService.ScanDrive(SelectedDrive), parallelOptions, (model, _) => TestParallel(model, _scanCancellationTokenSource.Token));
        }, _scanCancellationTokenSource.Token);

        await scanTask;
        CurrentScanMode = ScanMode.CanStartScan;
    }

    [RelayCommand(CanExecute = nameof(IsScanRunning))]
    public void PauseScan() {
        if (!IsScanRunning()) {
            return;
        }

        _pauseTokenSource.IsPaused = true;
        CurrentScanMode = ScanMode.Paused;
    }

    async ValueTask TestParallel(FolderModel model, CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() => {
            FoldersModels.Add(model);
        });
        await _pauseTokenSource.WaitWhilePausedAsync();
    }
}

public enum ScanMode {
    CanStartScan,
    Scanning,
    Paused,
}

public class DiskService {
    const int TenMb = 10 * 1024 * 1024;

    static readonly Func<FileInfo, bool> HasFilesGreaterTenMbFilter = fileInfo => fileInfo.Length >= TenMb;

    readonly Func<FileInfo, bool>[] _filters = {
            HasFilesGreaterTenMbFilter
        };

    public string[] GetDrives() {
        try {
            return Environment.GetLogicalDrives();
        }
        catch (Exception exception) {
            return Array.Empty<string>();
        }
    }

    public async IAsyncEnumerable<FolderModel> ScanDrive(string driveName) {
        if (string.IsNullOrEmpty(driveName)) {
            throw new ArgumentNullException(nameof(driveName), "Root folder arg is null");
        }

        var folders = new ConcurrentStack<string>();
        folders.Push(driveName);


        await foreach (var folderModel in ProcessFolders(folders)) {
            yield return folderModel;
        }
    }

    async IAsyncEnumerable<FolderModel> ProcessFolders(ConcurrentStack<string> folders) {
        while (folders.TryPop(out var currentFolder)) {
            string[] childFolders;

            try {
                childFolders = Directory.GetDirectories(currentFolder);
            }
            catch (UnauthorizedAccessException) {
                continue;
            }
            catch (DirectoryNotFoundException) {
                continue;
            }

            if (childFolders.Length > 0) {
                foreach (var folder in childFolders) {
                    folders.Push(folder);
                }
            }

            var currentFolderInfo = GetFolderModel(currentFolder);
            if (currentFolderInfo != null) {
                yield return currentFolderInfo;
            }
        }
    }

    FolderModel GetFolderModel(string folderPath) {
        var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly).ToArray();
        var filesCount = files.Length;
        var totalFilesSize = 0L;
        var hasAppliedFilters = false;

        Parallel.ForEach(files, () => 0L, (file, loopState, localSize) => {
            var fileInfo = new FileInfo(file);
            if (!hasAppliedFilters && _filters.Any(filter => filter(fileInfo))) {
                hasAppliedFilters = true;
            }

            return localSize + fileInfo.Length;
        },
                         localSize => Interlocked.Add(ref totalFilesSize, localSize));

        //foreach (var file in files) {
        //    try {
        //        var fileInfo = new FileInfo(file);
        //        if (!fileInfo.Exists) {
        //            continue;
        //        }

        //        totalFilesSize += fileInfo.Length;

        //        if (!hasAppliedFilters && _filters.Any(filter => filter(fileInfo))) {
        //            hasAppliedFilters = true;
        //        }
        //    }
        //    catch (Exception) {
        //        // ignore
        //    }
        //}

        return hasAppliedFilters
                   ? new FolderModel {
                       FilesCount = filesCount,
                       FilesSize = totalFilesSize,
                       Path = folderPath
                   }
                   : null;
    }
}
