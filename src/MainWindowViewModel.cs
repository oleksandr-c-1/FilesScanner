using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FilesScanner.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FilesScanner;

public partial class MainWindowViewModel : ObservableObject {
    [ObservableProperty]
    string[] _drives;

    [ObservableProperty]
    string _selectedDrive;

    [ObservableProperty]
    ObservableCollection<FolderModel> _foldersModels = new();

    readonly DiskService _diskService;

    public MainWindowViewModel() {
        _diskService = new DiskService();
        _drives = _diskService.GetDrives();
        if (_drives is { Length: > 0 }) {
            SelectedDrive = _drives[0];
        }

    }

    [RelayCommand]
    public Task StartScan() {
        var scanTask = Task.Run(async () => {
            await foreach (var folderModel in _diskService.ScanDrive(SelectedDrive)) {
                Application.Current.Dispatcher.BeginInvoke(() => {
                    FoldersModels.Add(folderModel);
                });
            }
        });

        return scanTask;
    }
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

            foreach (var folder in childFolders) {
                folders.Push(folder);
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

        foreach (var file in files) {
            try {
                var fileInfo = new FileInfo(file);
                if (!fileInfo.Exists) {
                    continue;
                }

                totalFilesSize += fileInfo.Length;

                if (!hasAppliedFilters && _filters.Any(filter => filter(fileInfo))) {
                    hasAppliedFilters = true;
                }
            }
            catch (Exception) {
                // ignore
            }
        }

        return hasAppliedFilters
                   ? new FolderModel {
                       FilesCount = filesCount,
                       FilesSize = totalFilesSize,
                       Path = folderPath
                   }
                   : null;
    }

    //private void StartProcess() {
    //    //isPaused = IsTerminated = false;
    //    //isProcessing = true;

    //    var currentProcessCount = Environment.ProcessorCount;

    //    var result = Parallel.ForEach(items.Skip(lowestBreakIndex ?? 0) //I am assuming that items is some form of ICollection. The Skip function will skip to the next item to process
    //                                  , new ParallelOptions { MaxDegreeOfParallelism = currentProcessCount }, (item, state) => {
    //                                      //I swapped these two.
    //                                      if (isPaused)
    //                                          state.Break();
    //                                      else if (IsTerminated)
    //                                          state.Stop();
    //                                      else
    //                                          item.Process();
    //                                  });

    //    lowestBreakIndex = result.LowestBreakIteration;

    //    isPaused = IsTerminated = false;
    //    isProcessing = false;
    //}
}
