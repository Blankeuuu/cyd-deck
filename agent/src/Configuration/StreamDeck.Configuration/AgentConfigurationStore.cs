namespace StreamDeck.Configuration;

public sealed class AgentConfigurationStore
{
    public const string FileName = "config.json";

    private readonly string _path;
    private readonly AgentConfigurationValidator _validator;
    private readonly IConfigurationFileSystem _fileSystem;

    public AgentConfigurationStore(
        string path,
        AgentConfigurationValidator? validator = null,
        IConfigurationFileSystem? fileSystem = null)
    {
        _path = path;
        _validator = validator ?? new AgentConfigurationValidator();
        _fileSystem = fileSystem ?? new ConfigurationFileSystem();
    }

    public AgentConfiguration LoadOrDefault()
    {
        if (!_fileSystem.FileExists(_path))
        {
            return AgentConfiguration.CreateDefault();
        }

        return Load();
    }

    public AgentConfiguration Load()
    {
        var configuration = AgentConfigurationJson.Deserialize(_fileSystem.ReadAllText(_path));
        ValidateOrThrow(configuration);
        return configuration;
    }

    public void Save(AgentConfiguration configuration)
    {
        configuration = ConfigurationColorNormalizer.Normalize(configuration);
        ValidateOrThrow(configuration);

        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            _fileSystem.CreateDirectory(directory);
        }

        var tempPath = Path.Combine(
            directory ?? string.Empty,
            $"{Path.GetFileName(_path)}.{Guid.NewGuid():N}.tmp");

        try
        {
            _fileSystem.WriteAllText(tempPath, AgentConfigurationJson.Serialize(configuration));

            if (_fileSystem.FileExists(_path))
            {
                _fileSystem.ReplaceFile(tempPath, _path, null);
            }
            else
            {
                _fileSystem.MoveFile(tempPath, _path, overwrite: false);
            }
        }
        finally
        {
            _fileSystem.DeleteFile(tempPath);
        }
    }

    private void ValidateOrThrow(AgentConfiguration configuration)
    {
        var issues = _validator.Validate(configuration);
        if (issues.Count > 0)
        {
            throw new AgentConfigurationException(issues);
        }
    }
}
