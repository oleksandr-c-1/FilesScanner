using FilesScanner.Interfaces;
using FilesScanner.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FilesScanner.Services;

public class DiskService : IDiskService {
    readonly ILogger<DiskService> _logger;
    const int TenMb = 10 * 1024 * 1024;

    static readonly Func<FileInfo, bool> HasFilesGreaterTenMbFilter = fileInfo => fileInfo.Length >= TenMb;

    readonly Func<FileInfo, bool>[] _filters = {
        HasFilesGreaterTenMbFilter
    };

    public DiskService(ILogger<DiskService> logger) {
        _logger = logger;
    }

    public string[] GetDrives() {
        try {
            return Environment.GetLogicalDrives();
        }
        catch (Exception exception) {
            _logger.LogInformation($"Can't get logical drivers. Error => {exception}");
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

#pragma warning disable CS1998 // Async method lacks 'await' operators and will run synchronously
    async IAsyncEnumerable<FolderModel> ProcessFolders(ConcurrentStack<string> folders) {
#pragma warning restore CS1998 // Async method lacks 'await' operators and will run synchronously
        while (folders.TryPop(out var currentFolder)) {
            string[] childFolders;

            try {
                childFolders = Directory.GetDirectories(currentFolder);
            }
            catch (UnauthorizedAccessException) {
                _logger.LogDebug($"Unauthorized access => {currentFolder}");
                continue;
            }
            catch (DirectoryNotFoundException) {
                _logger.LogDebug($"Directory not found => {currentFolder}");
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

        Parallel.ForEach(files, () => 0L, (file, _, localSize) => {
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