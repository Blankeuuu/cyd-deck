using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace StreamDeck.WindowsAgent.Updates;

public sealed class ArduinoOtaFirmwareUpdater
{
    public const int DefaultOtaPort = 3232;
    public const int MaxFirmwareBytes = 0x1F0000;

    private const int FlashCommand = 0;
    private const int AuthCommand = 200;
    private const int ChunkSize = 1024;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    public async Task<FirmwareUpdateResult> UploadAsync(
        FirmwareUpdateRequest request,
        IProgress<FirmwareUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (!OperatingSystem.IsWindows())
        {
            return FirmwareUpdateResult.Failed("Firmware update is only available on Windows.");
        }

        var validation = await FirmwareImageValidator.ReadAndValidateAsync(
            request.OpenFirmwareStream,
            MaxFirmwareBytes,
            cancellationToken).ConfigureAwait(false);
        if (!validation.IsSuccess)
        {
            return FirmwareUpdateResult.Failed(validation.SafeError);
        }

        var image = validation.Image;
        try
        {
            if (request.ExpectedSha256 is not null && !string.Equals(
                Convert.ToHexString(SHA256.HashData(image)), request.ExpectedSha256, StringComparison.OrdinalIgnoreCase))
                return FirmwareUpdateResult.Failed("Suma SHA-256 firmware uległa zmianie. Pobierz pakiet ponownie.");
            progress?.Report(new FirmwareUpdateProgress(0, image.Length, "Starting firmware update"));
            await UploadValidatedImageAsync(request, image, progress, cancellationToken).ConfigureAwait(false);
            progress?.Report(new FirmwareUpdateProgress(image.Length, image.Length, "Firmware update complete"));
            return FirmwareUpdateResult.Successful();
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (FirmwareUpdateException exception)
        {
            return FirmwareUpdateResult.Failed(exception.Message);
        }
        finally
        {
            Array.Clear(image);
        }
    }

    public Task<FirmwareUpdateResult> UploadAsync(
        FirmwareUpdateFileRequest request,
        IProgress<FirmwareUpdateProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return UploadAsync(
            new FirmwareUpdateRequest(
                request.DeviceAddress,
                request.SetupPassword,
                () => File.OpenRead(request.FirmwarePath),
                request.DevicePort,
                request.Timeout,
                request.ExpectedSha256),
            progress,
            cancellationToken);
    }

    private static async Task UploadValidatedImageAsync(
        FirmwareUpdateRequest request,
        byte[] image,
        IProgress<FirmwareUpdateProgress>? progress,
        CancellationToken cancellationToken)
    {
        var firmwareMd5 = Convert.ToHexString(MD5.HashData(image)).ToLowerInvariant();
        using var listener = new TcpListener(IPAddress.Any, 0);
        listener.Start(backlog: 1);
        var localPort = ((IPEndPoint)listener.LocalEndpoint).Port;

        using var udp = new UdpClient(AddressFamily.InterNetwork);
        udp.Client.ReceiveTimeout = ToMilliseconds(request.Timeout);

        var invitation = Encoding.ASCII.GetBytes(
            string.Create(
                CultureInfo.InvariantCulture,
                $"{FlashCommand} {localPort} {image.Length} {firmwareMd5}\n"));

        var remoteEndpoint = new IPEndPoint(request.DeviceAddress, request.DevicePort);
        var invitationResult = await SendInvitationAsync(
            udp,
            remoteEndpoint,
            invitation,
            request.SetupPassword,
            image.Length,
            firmwareMd5,
            request.Timeout,
            cancellationToken).ConfigureAwait(false);

        if (!invitationResult)
        {
            throw new FirmwareUpdateException("Firmware update device did not accept the update request.");
        }

        progress?.Report(new FirmwareUpdateProgress(0, image.Length, "Firmware update accepted by device"));
        using var client = await AcceptDeviceAsync(listener, request.Timeout, cancellationToken).ConfigureAwait(false);
        await SendImageAsync(client, image, progress, request.Timeout, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<bool> SendInvitationAsync(
        UdpClient udp,
        IPEndPoint remoteEndpoint,
        byte[] invitation,
        string setupPassword,
        int imageLength,
        string firmwareMd5,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await udp.SendAsync(invitation, remoteEndpoint, cancellationToken).ConfigureAwait(false);
            var response = await ReceiveUdpTextAsync(udp, timeout, cancellationToken).ConfigureAwait(false);
            if (response is null)
            {
                continue;
            }

            if (string.Equals(response, "OK", StringComparison.Ordinal))
            {
                return true;
            }

            if (response.StartsWith("AUTH ", StringComparison.Ordinal))
            {
                var nonce = response[5..].Trim();
                if (nonce.Length != 32)
                {
                    throw new FirmwareUpdateException("Firmware update authentication challenge was invalid.");
                }

                var cnonce = ComputeHexMd5($"{imageLength}:{firmwareMd5}:{remoteEndpoint.Address}");
                var passwordHash = ComputeHexMd5(setupPassword);
                var challengeResponse = ComputeHexMd5($"{passwordHash}:{nonce}:{cnonce}");
                var authMessage = Encoding.ASCII.GetBytes(
                    string.Create(CultureInfo.InvariantCulture, $"{AuthCommand} {cnonce} {challengeResponse}\n"));
                await udp.SendAsync(authMessage, remoteEndpoint, cancellationToken).ConfigureAwait(false);
                var authResponse = await ReceiveUdpTextAsync(udp, timeout, cancellationToken).ConfigureAwait(false);
                if (string.Equals(authResponse, "OK", StringComparison.Ordinal))
                {
                    return true;
                }

                throw new FirmwareUpdateException("Firmware update authentication failed.");
            }

            throw new FirmwareUpdateException("Firmware update device returned an invalid response.");
        }

        return false;
    }

    private static async Task<string?> ReceiveUdpTextAsync(UdpClient udp, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            var result = await udp.ReceiveAsync(cancellationToken)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            return Encoding.ASCII.GetString(result.Buffer).Trim();
        }
        catch (TimeoutException)
        {
            return null;
        }
        catch (SocketException)
        {
            return null;
        }
    }

    private static async Task<TcpClient> AcceptDeviceAsync(
        TcpListener listener,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        try
        {
            return await listener.AcceptTcpClientAsync(cancellationToken)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (TimeoutException exception)
        {
            throw new FirmwareUpdateException("Firmware update device did not connect back to the uploader.", exception);
        }
    }

    private static async Task SendImageAsync(
        TcpClient client,
        byte[] image,
        IProgress<FirmwareUpdateProgress>? progress,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using (client)
        {
            client.NoDelay = true;
            await using var stream = client.GetStream();
            var offset = 0;
            var lastResponseWasOk = false;
            while (offset < image.Length)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(ChunkSize, image.Length - offset);
                await stream.WriteAsync(image.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

                var response = await ReadTcpResponseAsync(stream, timeout, cancellationToken).ConfigureAwait(false);
                if (string.Equals(response, "OK", StringComparison.Ordinal))
                {
                    lastResponseWasOk = true;
                }
                else if (!int.TryParse(response, NumberStyles.None, CultureInfo.InvariantCulture, out var accepted) || accepted <= 0)
                {
                    throw new FirmwareUpdateException("Firmware update device rejected uploaded data.");
                }

                offset += count;
                progress?.Report(new FirmwareUpdateProgress(offset, image.Length, "Uploading firmware"));
            }

            if (lastResponseWasOk)
            {
                return;
            }

            for (var attempt = 0; attempt < 5; attempt++)
            {
                var response = await ReadTcpResponseAsync(stream, TimeSpan.FromSeconds(60), cancellationToken).ConfigureAwait(false);
                if (string.Equals(response, "OK", StringComparison.Ordinal))
                {
                    return;
                }
            }

            throw new FirmwareUpdateException("Firmware update device did not confirm completion.");
        }
    }

    private static async Task<string> ReadTcpResponseAsync(
        NetworkStream stream,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(32);
        try
        {
            var count = await stream.ReadAsync(buffer.AsMemory(0, 32), cancellationToken)
                .AsTask()
                .WaitAsync(timeout, cancellationToken)
                .ConfigureAwait(false);
            if (count <= 0)
            {
                throw new FirmwareUpdateException("Firmware update connection closed unexpectedly.");
            }

            return Encoding.ASCII.GetString(buffer, 0, count).Trim();
        }
        catch (TimeoutException exception)
        {
            throw new FirmwareUpdateException("Firmware update timed out while waiting for the device.", exception);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private static string ComputeHexMd5(string value)
    {
        return Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    }

    private static int ToMilliseconds(TimeSpan timeout)
    {
        var effective = timeout <= TimeSpan.Zero ? DefaultTimeout : timeout;
        return (int)Math.Clamp(effective.TotalMilliseconds, 1, int.MaxValue);
    }
}

public sealed class FirmwareUpdateRequest
{
    public FirmwareUpdateRequest(
        IPAddress deviceAddress,
        string setupPassword,
        Func<Stream> openFirmwareStream,
        int devicePort = ArduinoOtaFirmwareUpdater.DefaultOtaPort,
        TimeSpan? timeout = null,
        string? expectedSha256 = null)
    {
        DeviceAddress = deviceAddress;
        SetupPassword = setupPassword;
        OpenFirmwareStream = openFirmwareStream;
        ExpectedSha256 = expectedSha256;
        DevicePort = devicePort;
        Timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public IPAddress DeviceAddress { get; }

    public string SetupPassword { get; }

    public Func<Stream> OpenFirmwareStream { get; }
    public string? ExpectedSha256 { get; }

    public int DevicePort { get; }

    public TimeSpan Timeout { get; }
}

public sealed class FirmwareUpdateFileRequest
{
    public FirmwareUpdateFileRequest(
        IPAddress deviceAddress,
        string setupPassword,
        string firmwarePath,
        int devicePort = ArduinoOtaFirmwareUpdater.DefaultOtaPort,
        TimeSpan? timeout = null,
        string? expectedSha256 = null)
    {
        DeviceAddress = deviceAddress;
        SetupPassword = setupPassword;
        FirmwarePath = firmwarePath;
        ExpectedSha256 = expectedSha256;
        DevicePort = devicePort;
        Timeout = timeout ?? TimeSpan.FromSeconds(10);
    }

    public IPAddress DeviceAddress { get; }

    public string SetupPassword { get; }

    public string FirmwarePath { get; }
    public string? ExpectedSha256 { get; }

    public int DevicePort { get; }

    public TimeSpan Timeout { get; }
}

public sealed record FirmwareUpdateProgress(long BytesSent, long TotalBytes, string Stage);

public sealed record FirmwareUpdateResult(bool Success, string? SafeError)
{
    public static FirmwareUpdateResult Successful() => new(true, null);

    public static FirmwareUpdateResult Failed(string safeError) => new(false, safeError);
}

public sealed class FirmwareUpdateException : Exception
{
    public FirmwareUpdateException(string message)
        : base(message)
    {
    }

    public FirmwareUpdateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal static class FirmwareImageValidator
{
    private const byte EspFirmwareMagic = 0xE9;

    public static async Task<FirmwareImageValidationResult> ReadAndValidateAsync(
        Func<Stream> openStream,
        int maxBytes,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(openStream);

        await using var stream = openStream();
        if (stream is null)
        {
            return FirmwareImageValidationResult.Failed("Firmware update image could not be opened.");
        }

        using var buffer = new MemoryStream(capacity: Math.Min(maxBytes, 1024 * 1024));
        var rented = ArrayPool<byte>.Shared.Rent(16 * 1024);
        var total = 0;
        try
        {
            while (true)
            {
                var read = await stream.ReadAsync(rented, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                total += read;
                if (total > maxBytes)
                {
                    return FirmwareImageValidationResult.Failed("Firmware update image is too large for the OTA slot.");
                }

                buffer.Write(rented, 0, read);
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }

        var image = buffer.ToArray();
        if (image.Length == 0)
        {
            return FirmwareImageValidationResult.Failed("Firmware update image is empty.");
        }

        if (image[0] != EspFirmwareMagic)
        {
            Array.Clear(image);
            return FirmwareImageValidationResult.Failed("Firmware update image is not a valid ESP firmware image.");
        }

        return FirmwareImageValidationResult.Success(image);
    }
}

internal sealed record FirmwareImageValidationResult(
    bool IsSuccess,
    byte[] Image,
    string SafeError)
{
    public static FirmwareImageValidationResult Success(byte[] image) => new(true, image, string.Empty);

    public static FirmwareImageValidationResult Failed(string safeError) => new(false, [], safeError);
}
