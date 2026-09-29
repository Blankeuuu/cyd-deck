namespace StreamDeck.Configuration;

public sealed class ConfigurationFileSystem : IConfigurationFileSystem
{
    public bool FileExists(string path)
    {
        return File.Exists(path);
    }

    public void CreateDirectory(string path)
    {
        Directory.CreateDirectory(path);
    }

    public string ReadAllText(string path)
    {
        return File.ReadAllText(path);
    }

    public void WriteAllText(string path, string contents)
    {
        File.WriteAllText(path, contents);
    }

    public void ReplaceFile(string sourceFileName, string destinationFileName, string? destinationBackupFileName)
    {
        File.Replace(sourceFileName, destinationFileName, destinationBackupFileName);
    }

    public void MoveFile(string sourceFileName, string destinationFileName, bool overwrite)
    {
        File.Move(sourceFileName, destinationFileName, overwrite);
    }

    public void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
