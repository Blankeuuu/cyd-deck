namespace StreamDeck.Protocol;

public sealed record ProtocolParseResult(
    bool IsSuccess,
    ProtocolEnvelope? Envelope,
    ProtocolErrorCode? ErrorCode,
    string? ErrorMessage)
{
    public static ProtocolParseResult Success(ProtocolEnvelope envelope)
    {
        return new ProtocolParseResult(true, envelope, null, null);
    }

    public static ProtocolParseResult Failure(ProtocolErrorCode code, string message)
    {
        return new ProtocolParseResult(false, null, code, message);
    }
}
