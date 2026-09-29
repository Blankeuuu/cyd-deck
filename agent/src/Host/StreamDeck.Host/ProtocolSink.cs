namespace StreamDeck.Host;

public interface IProtocolSink
{
    ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default);
}

public sealed class MemoryProtocolSink : IProtocolSink
{
    private readonly List<string> messages = [];

    public IReadOnlyList<string> Messages => messages;

    public ValueTask SendTextAsync(string message, CancellationToken cancellationToken = default)
    {
        messages.Add(message);
        return ValueTask.CompletedTask;
    }
}
