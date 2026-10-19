using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using StreamDeck.Protocol;

namespace StreamDeck.Host;

public sealed class PairingApprovalService : IPairingApprovalService
{
    private const int TokenBytes = 32;

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly object gate = new();
    private readonly string storagePath;
    private readonly Dictionary<string, PairingRecord> records;
    private readonly Dictionary<string, string> undeliveredTokens = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Guid> pendingSessionIds = new(StringComparer.Ordinal);
    private readonly TimeProvider timeProvider;
    private readonly IPairingTokenProtector tokenProtector;

    public PairingApprovalService(string? storagePath = null, TimeProvider? timeProvider = null)
        : this(storagePath, timeProvider, new DpapiPairingTokenProtector())
    {
    }

    internal PairingApprovalService(string? storagePath, TimeProvider? timeProvider, IPairingTokenProtector tokenProtector)
    {
        this.storagePath = storagePath ?? CreateDefaultStoragePath();
        this.timeProvider = timeProvider ?? TimeProvider.System;
        this.tokenProtector = tokenProtector;
        records = LoadRecords(this.storagePath);
    }

    public event EventHandler? PairingChanged;

    public ValueTask<DeviceAuthorizationResult> AuthorizeAsync(
        HelloMessage hello,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        return AuthorizeAsync(hello, bearerToken, remoteAddress: null, cancellationToken);
    }

    public ValueTask<DeviceAuthorizationResult> AuthorizeAsync(
        HelloMessage hello,
        string? bearerToken,
        string? remoteAddress,
        CancellationToken cancellationToken = default)
    {
        return AuthorizeAsync(hello, bearerToken, remoteAddress, pendingSessionId: null, cancellationToken);
    }

    internal ValueTask<DeviceAuthorizationResult> AuthorizeAsync(
        HelloMessage hello,
        string? bearerToken,
        string? remoteAddress,
        Guid? pendingSessionId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(hello);

        if (!string.Equals(hello.Role, "esp32", StringComparison.Ordinal))
        {
            return ValueTask.FromResult(DeviceAuthorizationResult.Rejected);
        }

        DeviceAuthorizationResult result;
        var changed = false;

        lock (gate)
        {
            if (!records.TryGetValue(hello.DeviceId, out var record))
            {
                record = new PairingRecord(
                    hello.DeviceId,
                    hello.Name,
                    remoteAddress,
                    timeProvider.GetUtcNow(),
                    PairingState.Pending,
                    ProtectedToken: null);
                records.Add(hello.DeviceId, record);
                RememberPendingSession(hello.DeviceId, pendingSessionId);
                PersistRecords();
                changed = true;
            }
            if (record.State == PairingState.Pending && IsDuplicatePendingSession(hello.DeviceId, pendingSessionId))
            {
                result = DeviceAuthorizationResult.Rejected;
            }
            else
            {
                if (record.State == PairingState.Pending && ShouldRefreshPending(record, hello.Name, remoteAddress))
                {
                    records[hello.DeviceId] = record with { Name = hello.Name, RemoteAddress = remoteAddress ?? record.RemoteAddress };
                    RememberPendingSession(hello.DeviceId, pendingSessionId);
                    PersistRecords();
                    changed = true;
                }

                result = records[hello.DeviceId].State switch
                {
                    PairingState.Approved => IsBearerTokenValid(records[hello.DeviceId], bearerToken)
                        ? DeviceAuthorizationResult.Authorized
                        : DeviceAuthorizationResult.Rejected,
                    PairingState.Rejected => DeviceAuthorizationResult.Rejected,
                    _ => DeviceAuthorizationResult.PendingApproval
                };
            }
        }

        if (changed)
        {
            OnPairingChanged();
        }

        return ValueTask.FromResult(result);
    }

    public Task<IReadOnlyList<PendingDevice>> ListPendingAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<PendingDevice>>(records.Values
                .Where(record => record.State == PairingState.Pending)
                .OrderBy(record => record.FirstSeenUtc)
                .Select(record => new PendingDevice(record.DeviceId, record.Name, record.RemoteAddress, record.FirstSeenUtc))
                .ToArray());
        }
    }

    public Task<IReadOnlyList<PairedDevice>> ListApprovedAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (gate)
        {
            return Task.FromResult<IReadOnlyList<PairedDevice>>(records.Values
                .Where(record => record.State == PairingState.Approved)
                .OrderBy(record => record.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(record => record.DeviceId, StringComparer.Ordinal)
                .Select(record => new PairedDevice(record.DeviceId, record.Name, record.RemoteAddress, record.FirstSeenUtc))
                .ToArray());
        }
    }

    public Task<bool> ApproveAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        string token;
        lock (gate)
        {
            if (!records.TryGetValue(deviceId, out var record) || record.State != PairingState.Pending)
            {
                return Task.FromResult(false);
            }

            token = CreateBearerToken();
            records[deviceId] = record with
            {
                State = PairingState.Approved,
                ProtectedToken = tokenProtector.Protect(token)
            };
            undeliveredTokens[CreatePendingTokenKey(deviceId, GetPendingSessionId(deviceId))] = token;
            pendingSessionIds.Remove(deviceId);
            PersistRecords();
        }

        OnPairingChanged();
        return Task.FromResult(true);
    }

    public Task<bool> RejectAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        lock (gate)
        {
            if (!records.TryGetValue(deviceId, out var record) || record.State != PairingState.Pending)
            {
                return Task.FromResult(false);
            }

            records[deviceId] = record with { State = PairingState.Rejected, ProtectedToken = null };
            RemoveUndeliveredTokens(deviceId);
            pendingSessionIds.Remove(deviceId);
            PersistRecords();
        }

        OnPairingChanged();
        return Task.FromResult(true);
    }

    public Task<bool> ForgetAsync(string deviceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        var changed = false;
        lock (gate)
        {
            changed = records.Remove(deviceId);
            changed = RemoveUndeliveredTokens(deviceId) || changed;
            changed = pendingSessionIds.Remove(deviceId) || changed;
            if (!changed)
            {
                return Task.FromResult(false);
            }

            PersistRecords();
        }

        OnPairingChanged();
        return Task.FromResult(true);
    }

    internal string? TryConsumeUndeliveredToken(string deviceId)
    {
        return TryConsumeUndeliveredToken(deviceId, pendingSessionId: null);
    }

    internal string? TryConsumeUndeliveredToken(string deviceId, Guid? pendingSessionId)
    {
        lock (gate)
        {
            var key = CreatePendingTokenKey(deviceId, pendingSessionId);
            return undeliveredTokens.Remove(key, out var token) ? token : null;
        }
    }

    internal bool IsRejected(string deviceId)
    {
        lock (gate)
        {
            return records.TryGetValue(deviceId, out var record) && record.State == PairingState.Rejected;
        }
    }

    private static bool ShouldRefreshPending(PairingRecord record, string name, string? remoteAddress)
    {
        return !string.Equals(record.Name, name, StringComparison.Ordinal)
            || (remoteAddress is not null && !string.Equals(record.RemoteAddress, remoteAddress, StringComparison.Ordinal));
    }

    private void RememberPendingSession(string deviceId, Guid? pendingSessionId)
    {
        if (pendingSessionId.HasValue)
        {
            pendingSessionIds[deviceId] = pendingSessionId.Value;
        }
    }

    private bool IsDuplicatePendingSession(string deviceId, Guid? pendingSessionId)
    {
        return pendingSessionId.HasValue
            && pendingSessionIds.TryGetValue(deviceId, out var existingSessionId)
            && existingSessionId != pendingSessionId.Value;
    }

    private Guid? GetPendingSessionId(string deviceId)
    {
        return pendingSessionIds.TryGetValue(deviceId, out var pendingSessionId) ? pendingSessionId : null;
    }

    private bool RemoveUndeliveredTokens(string deviceId)
    {
        var removed = false;
        foreach (var key in undeliveredTokens.Keys.Where(key => key == deviceId || key.StartsWith(deviceId + ":", StringComparison.Ordinal)).ToArray())
        {
            removed = undeliveredTokens.Remove(key) || removed;
        }

        return removed;
    }

    private static string CreatePendingTokenKey(string deviceId, Guid? pendingSessionId)
    {
        return pendingSessionId.HasValue ? $"{deviceId}:{pendingSessionId.Value:N}" : deviceId;
    }

    private static string CreateBearerToken()
    {
        Span<byte> bytes = stackalloc byte[TokenBytes];
        do
        {
            RandomNumberGenerator.Fill(bytes);
        } while ((bytes[0] >> 2) >= 62);
        return Base64UrlEncode(bytes);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[]? Base64UrlDecode(string? token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var padded = token.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');

        try
        {
            return Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private bool IsBearerTokenValid(PairingRecord record, string? bearerToken)
    {
        if (record.ProtectedToken is null)
        {
            return false;
        }

        var candidateBytes = Base64UrlDecode(bearerToken);
        if (candidateBytes is null || candidateBytes.Length != TokenBytes)
        {
            return false;
        }

        var storedBytes = Base64UrlDecode(tokenProtector.Unprotect(record.ProtectedToken));
        return storedBytes is not null
            && storedBytes.Length == TokenBytes
            && CryptographicOperations.FixedTimeEquals(storedBytes, candidateBytes);
    }

    private static string CreateDefaultStoragePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(appData, "StreamDeck", "Host", "pairing.json");
    }

    private static Dictionary<string, PairingRecord> LoadRecords(string path)
    {
        if (!File.Exists(path))
        {
            return new Dictionary<string, PairingRecord>(StringComparer.Ordinal);
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (stream.Length == 0)
                {
                    Thread.Sleep(10);
                    continue;
                }
                var store = JsonSerializer.Deserialize<PairingStore>(stream, JsonOptions);
                return store?.Devices?.ToDictionary(record => record.DeviceId, StringComparer.Ordinal)
                    ?? new Dictionary<string, PairingRecord>(StringComparer.Ordinal);
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(10);
            }
        }

        return new Dictionary<string, PairingRecord>(StringComparer.Ordinal);
    }

    private void PersistRecords()
    {
        var directory = Path.GetDirectoryName(storagePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var store = new PairingStore(records.Values.OrderBy(record => record.DeviceId, StringComparer.Ordinal).ToArray());
        using var stream = new FileStream(storagePath, FileMode.Create, FileAccess.Write, FileShare.Read);
        JsonSerializer.Serialize(stream, store, JsonOptions);
        stream.Flush(flushToDisk: true);
    }

    private void OnPairingChanged()
    {
        PairingChanged?.Invoke(this, EventArgs.Empty);
    }

    private sealed record PairingStore(IReadOnlyList<PairingRecord> Devices);

    private sealed record PairingRecord(
        string DeviceId,
        string Name,
        string? RemoteAddress,
        DateTimeOffset FirstSeenUtc,
        PairingState State,
        string? ProtectedToken);

    private enum PairingState
    {
        Pending,
        Approved,
        Rejected
    }
}

internal interface IPairingTokenProtector
{
    string Protect(string token);

    string Unprotect(string protectedToken);
}

internal sealed class DpapiPairingTokenProtector : IPairingTokenProtector
{
    private const string CurrentUserPrefix = "dpapi-current-user-v1:";
    private const string LocalMachinePrefix = "dpapi-local-machine-v1:";

    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("StreamDeck.Host.PairingToken.v1");

    public string Protect(string token)
    {
        var tokenBytes = Encoding.UTF8.GetBytes(token);

        try
        {
            return CurrentUserPrefix + Protect(tokenBytes, DataProtectionScope.CurrentUser);
        }
        catch (CryptographicException)
        {
            return LocalMachinePrefix + Protect(tokenBytes, DataProtectionScope.LocalMachine);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public string Unprotect(string protectedToken)
    {
        if (protectedToken.StartsWith(CurrentUserPrefix, StringComparison.Ordinal))
        {
            return Unprotect(protectedToken[CurrentUserPrefix.Length..], Entropy, DataProtectionScope.CurrentUser);
        }

        if (protectedToken.StartsWith(LocalMachinePrefix, StringComparison.Ordinal))
        {
            return Unprotect(protectedToken[LocalMachinePrefix.Length..], Entropy, DataProtectionScope.LocalMachine);
        }

        return Unprotect(protectedToken, optionalEntropy: null, DataProtectionScope.CurrentUser);
    }

    private static string Protect(byte[] tokenBytes, DataProtectionScope scope)
    {
        var protectedBytes = ProtectedData.Protect(tokenBytes, Entropy, scope);
        return Convert.ToBase64String(protectedBytes);
    }

    private static string Unprotect(string protectedToken, byte[]? optionalEntropy, DataProtectionScope scope)
    {
        var tokenBytes = ProtectedData.Unprotect(Convert.FromBase64String(protectedToken), optionalEntropy, scope);

        try
        {
            return Encoding.UTF8.GetString(tokenBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }
}
