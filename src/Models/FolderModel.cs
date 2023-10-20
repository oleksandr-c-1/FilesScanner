namespace FilesScanner.Models;

public class FolderModel {
    public string Path { get; set; }
    public int FilesCount { get; set; }
    public long FilesSize { get; set; }
}