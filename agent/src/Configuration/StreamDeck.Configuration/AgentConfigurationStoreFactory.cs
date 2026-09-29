namespace StreamDeck.Configuration;

public static class AgentConfigurationStoreFactory
{
    public static AgentConfigurationStore CreateForCurrentUser()
    {
        var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var configPath = Path.Combine(basePath, "StreamDeckAgent", AgentConfigurationStore.FileName);
        return new AgentConfigurationStore(configPath);
    }
}
