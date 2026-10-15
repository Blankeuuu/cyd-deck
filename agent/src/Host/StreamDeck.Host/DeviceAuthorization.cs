using StreamDeck.Protocol;

namespace StreamDeck.Host;

public interface IDeviceAuthorizer
{
    ValueTask<DeviceAuthorizationResult> AuthorizeAsync(
        HelloMessage hello,
        string? bearerToken,
        CancellationToken cancellationToken = default);
}

public sealed record DeviceAuthorizationResult(DeviceAuthorizationStatus Status, string? Detail = null)
{
    public static DeviceAuthorizationResult Authorized { get; } = new(DeviceAuthorizationStatus.Authorized);

    public static DeviceAuthorizationResult PendingApproval { get; } = new(DeviceAuthorizationStatus.PendingApproval, "Device is waiting for pairing approval.");

    public static DeviceAuthorizationResult Rejected { get; } = new(DeviceAuthorizationStatus.Rejected, "Device is not paired.");
}

public enum DeviceAuthorizationStatus
{
    Authorized,
    PendingApproval,
    Rejected
}

public interface IPairingApprovalService : IDeviceAuthorizer
{
    Task<IReadOnlyList<PendingDevice>> ListPendingAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PairedDevice>> ListApprovedAsync(CancellationToken cancellationToken = default);

    Task<bool> ApproveAsync(string deviceId, CancellationToken cancellationToken = default);

    Task<bool> RejectAsync(string deviceId, CancellationToken cancellationToken = default);

    Task<bool> ForgetAsync(string deviceId, CancellationToken cancellationToken = default);

    event EventHandler? PairingChanged;
}

public sealed record PendingDevice(string DeviceId, string Name, string? RemoteAddress, DateTimeOffset FirstSeenUtc);

public sealed record PairedDevice(string DeviceId, string Name, string? RemoteAddress, DateTimeOffset FirstSeenUtc);

public sealed class RejectingDeviceAuthorizer : IDeviceAuthorizer
{
    public ValueTask<DeviceAuthorizationResult> AuthorizeAsync(
        HelloMessage hello,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(DeviceAuthorizationResult.PendingApproval);
    }
}
