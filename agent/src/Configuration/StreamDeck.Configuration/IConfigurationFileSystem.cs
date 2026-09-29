namespace StreamDeck.Configuration;

public interface IConfigurationFileSystem
{
    bool FileExists(string path);

    void CreateDirectory(string path);

    string ReadAllText(string path);

    void WriteAllText(string path, string contents);

    void ReplaceFile(string sourceFileName, string destinationFileName, string? destinationBackupFileName);

    void MoveFile(string sourceFileName, string destinationFileName, bool overwrite);

    void DeleteFile(string path);
}
