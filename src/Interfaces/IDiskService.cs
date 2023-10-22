using FilesScanner.Models;
using System.Collections.Generic;

namespace FilesScanner.Interfaces;

public interface IDiskService {
    string[] GetDrives();
    IAsyncEnumerable<FolderModel> ScanDrive(string driveName);
}