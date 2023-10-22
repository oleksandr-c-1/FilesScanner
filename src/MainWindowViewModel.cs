using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesScanner.Helpers;
using FilesScanner.Interfaces;
using FilesScanner.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using Application = System.Windows.Application;

namespace FilesScanner;

public partial class MainWindowViewModel : ObservableObject {
    readonly ILogger<MainWindowViewModel> _logger;
    readonly IDiskService _diskService;
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

    public MainWindowViewModel(ILogger<MainWindowViewModel> logger, IDiskService diskService) {
        _logger = logger;
        _diskService = diskService;
        LoadDrives();

        CurrentScanMode = ScanMode.CanStartScan;
    }

    // Note: Could be initialized in separate thread to prevent UI freezes on startup.  
    void LoadDrives() {
        Drives = _diskService.GetDrives();
        if (Drives is { Length: > 0 }) {
            SelectedDrive = Drives[0];
        }

        _logger.LogInformation($"Loaded {Drives.Length} drives");
    }

    bool IsScanRunning() {
        return CurrentScanMode == ScanMode.Scanning;
    }

    bool CanCancelScan() {
        return CurrentScanMode != ScanMode.CanStartScan;
    }

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    void CancelScan() {
        try {
            if (!_scanCancellationTokenSource.IsCancellationRequested) {
                _scanCancellationTokenSource.Cancel();
                _scanCancellationManualResetEventSlim.Wait(_scanCancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException) {
            // ignore
        }
        catch (Exception exception) {
            _logger.LogError($"Can't cancel scan. Error => {exception}");
        }
        finally {
            ResetTokens();

            FoldersModels.Clear();
            CurrentScanMode = ScanMode.CanStartScan;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    async Task StartScan() {
        try {
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

            var scanToken = _scanCancellationTokenSource.Token;
            var scanTask = Task.Run(async () => {
                var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = scanToken };
                await Parallel.ForEachAsync(_diskService.ScanDrive(SelectedDrive), parallelOptions, ProcessFileModel);
            }, scanToken);

            await scanTask;
            CurrentScanMode = ScanMode.CanStartScan;
        }
        catch (OperationCanceledException) {
            //ignore
        }
        catch (Exception exception) {
            _logger.LogError($"Scan error => {exception}");
        }
    }

    [RelayCommand(CanExecute = nameof(IsScanRunning))]
    void PauseScan() {
        if (!IsScanRunning()) {
            return;
        }

        _pauseTokenSource.IsPaused = true;
        CurrentScanMode = ScanMode.Paused;
    }

    async ValueTask ProcessFileModel(FolderModel model, CancellationToken cancellationToken) {
        if (cancellationToken.IsCancellationRequested) {
            return;
        }

        Application.Current.Dispatcher.BeginInvoke(() => {
            FoldersModels.Add(model);
        });
        await _pauseTokenSource.WaitWhilePausedAsync();
    }

    void ResetTokens() {
        if (_scanCancellationTokenSource != null) {
            _scanCancellationTokenSource.Dispose();
            _scanCancellationTokenSource = null;
        }

        if (_pauseTokenSource.IsPaused) {
            _pauseTokenSource.IsPaused = false;
        }
    }
}