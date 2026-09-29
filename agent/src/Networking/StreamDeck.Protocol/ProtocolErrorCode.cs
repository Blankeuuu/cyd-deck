namespace StreamDeck.Protocol;

public enum ProtocolErrorCode
{
    InvalidJson,
    MissingProtocol,
    UnsupportedProtocol,
    MissingType,
    UnknownType,
    MissingRequestId,
    InvalidRequestId,
    InvalidPayload
}
