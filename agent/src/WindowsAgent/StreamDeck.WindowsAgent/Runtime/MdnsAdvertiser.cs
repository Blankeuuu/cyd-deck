using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed class MdnsAdvertiser(int servicePort, IReadOnlyList<IPAddress>? addresses = null) : IAsyncDisposable
{
    public const string ServiceType = "_streamdeck._tcp.local";

    private static readonly IPAddress MulticastAddress = IPAddress.Parse("224.0.0.251");
    private static readonly IPEndPoint MulticastEndPoint = new(MulticastAddress, 5353);
    private readonly IReadOnlyList<IPAddress> addresses = addresses ?? GetDefaultAddresses();
    private readonly string instanceName = $"{SanitizeLabel(Environment.MachineName)} Stream Deck.{ServiceType}";
    private readonly string hostName = $"{SanitizeLabel(Environment.MachineName)}.local";
    private Socket? socket;
    private CancellationTokenSource? cancellation;
    private Task? receiveTask;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (socket is not null)
        {
            return;
        }

        cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ExclusiveAddressUse = false
        };
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
        socket.Bind(new IPEndPoint(IPAddress.Any, 5353));
        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(MulticastAddress, IPAddress.Any));

        receiveTask = Task.Run(() => ReceiveLoop(cancellation.Token), CancellationToken.None);
        await SendAnnouncementAsync(ttl: 120, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (socket is null)
        {
            return;
        }

        try
        {
            await SendAnnouncementAsync(ttl: 0, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
        }

        if (cancellation is not null)
        {
            await cancellation.CancelAsync().ConfigureAwait(false);
        }

        socket.Dispose();
        socket = null;

        if (receiveTask is not null)
        {
            try
            {
                await receiveTask.ConfigureAwait(false);
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
            catch (OperationCanceledException)
            {
            }

            receiveTask = null;
        }

        cancellation?.Dispose();
        cancellation = null;
    }

    private void ReceiveLoop(CancellationToken cancellationToken)
    {
        var buffer = new byte[1500];
        while (!cancellationToken.IsCancellationRequested && socket is not null)
        {
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            var length = socket.ReceiveFrom(buffer, ref remote);
            var questions = DnsMessage.TryReadQuestions(buffer.AsSpan(0, length));
            if (questions.Count == 0 || !ShouldAnswer(questions))
            {
                continue;
            }

            var response = CreateResponse(ttl: 120);
            var endpoint = questions.Any(question => question.UnicastResponse) ? remote : MulticastEndPoint;
            socket.SendTo(response, endpoint);
        }
    }

    private bool ShouldAnswer(IReadOnlyList<DnsQuestion> questions)
    {
        return questions.Any(question =>
            question.Type is DnsRecordType.Ptr or DnsRecordType.Srv or DnsRecordType.Txt or DnsRecordType.A or DnsRecordType.Any
            && (DnsName.Equals(question.Name, ServiceType)
                || DnsName.Equals(question.Name, instanceName)
                || DnsName.Equals(question.Name, hostName)));
    }

    private Task SendAnnouncementAsync(uint ttl, CancellationToken cancellationToken)
    {
        var currentSocket = socket;
        if (currentSocket is null)
        {
            return Task.CompletedTask;
        }

        cancellationToken.ThrowIfCancellationRequested();
        currentSocket.SendTo(CreateResponse(ttl), MulticastEndPoint);
        return Task.CompletedTask;
    }

    private byte[] CreateResponse(uint ttl)
    {
        var writer = new DnsMessageWriter();
        writer.WriteHeader(answerCount: (ushort)(3 + addresses.Count));
        writer.WritePtr(ServiceType, instanceName, ttl);
        writer.WriteSrv(instanceName, hostName, servicePort, ttl);
        writer.WriteTxt(instanceName, ["path=/ws", "proto=ws"], ttl);
        foreach (var address in addresses.Where(address => address.AddressFamily == AddressFamily.InterNetwork))
        {
            writer.WriteA(hostName, address, ttl);
        }

        return writer.ToArray();
    }

    private static IReadOnlyList<IPAddress> GetDefaultAddresses()
    {
        try
        {
            var addresses = Dns.GetHostAddresses(Dns.GetHostName())
                .Where(address => address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(address))
                .ToArray();
            return addresses.Length == 0 ? [IPAddress.Loopback] : addresses;
        }
        catch
        {
            return [IPAddress.Loopback];
        }
    }

    private static string SanitizeLabel(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value)
        {
            builder.Append(char.IsAsciiLetterOrDigit(character) || character is '-' ? character : '-');
            if (builder.Length == 48)
            {
                break;
            }
        }

        return builder.Length == 0 ? "streamdeck-agent" : builder.ToString().Trim('-');
    }
}

internal enum DnsRecordType : ushort
{
    A = 1,
    Ptr = 12,
    Txt = 16,
    Srv = 33,
    Any = 255
}

internal sealed record DnsQuestion(string Name, DnsRecordType Type, bool UnicastResponse);

internal static class DnsName
{
    public static bool Equals(string left, string right)
    {
        return string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);
    }

    private static string Normalize(string value)
    {
        return value.TrimEnd('.');
    }
}

internal static class DnsMessage
{
    public static IReadOnlyList<DnsQuestion> TryReadQuestions(ReadOnlySpan<byte> message)
    {
        if (message.Length < 12)
        {
            return [];
        }

        var questionCount = BinaryPrimitives.ReadUInt16BigEndian(message[4..6]);
        var offset = 12;
        var questions = new List<DnsQuestion>();
        for (var index = 0; index < questionCount; index++)
        {
            var name = ReadName(message, ref offset);
            if (name is null || offset + 4 > message.Length)
            {
                return questions;
            }

            var type = (DnsRecordType)BinaryPrimitives.ReadUInt16BigEndian(message.Slice(offset, 2));
            var queryClass = BinaryPrimitives.ReadUInt16BigEndian(message.Slice(offset + 2, 2));
            offset += 4;
            questions.Add(new DnsQuestion(name, type, (queryClass & 0x8000) != 0));
        }

        return questions;
    }

    private static string? ReadName(ReadOnlySpan<byte> message, ref int offset)
    {
        var labels = new List<string>();
        var position = offset;
        var jumped = false;
        for (var depth = 0; depth < 32; depth++)
        {
            if (position >= message.Length)
            {
                return null;
            }

            var length = message[position++];
            if (length == 0)
            {
                if (!jumped)
                {
                    offset = position;
                }

                return string.Join('.', labels);
            }

            if ((length & 0xC0) == 0xC0)
            {
                if (position >= message.Length)
                {
                    return null;
                }

                var pointer = ((length & 0x3F) << 8) | message[position++];
                if (!jumped)
                {
                    offset = position;
                    jumped = true;
                }

                position = pointer;
                continue;
            }

            if ((length & 0xC0) != 0 || position + length > message.Length)
            {
                return null;
            }

            labels.Add(Encoding.UTF8.GetString(message.Slice(position, length)));
            position += length;
        }

        return null;
    }
}

internal sealed class DnsMessageWriter
{
    private readonly MemoryStream stream = new();

    public void WriteHeader(ushort answerCount)
    {
        WriteUInt16(0);
        WriteUInt16(0x8400);
        WriteUInt16(0);
        WriteUInt16(answerCount);
        WriteUInt16(0);
        WriteUInt16(0);
    }

    public void WritePtr(string name, string pointer, uint ttl)
    {
        WriteRecord(name, DnsRecordType.Ptr, ttl, () => WriteName(pointer));
    }

    public void WriteSrv(string name, string target, int port, uint ttl)
    {
        WriteRecord(name, DnsRecordType.Srv, ttl, () =>
        {
            WriteUInt16(0);
            WriteUInt16(0);
            WriteUInt16((ushort)port);
            WriteName(target);
        });
    }

    public void WriteTxt(string name, IReadOnlyList<string> values, uint ttl)
    {
        WriteRecord(name, DnsRecordType.Txt, ttl, () =>
        {
            foreach (var value in values)
            {
                var bytes = Encoding.UTF8.GetBytes(value);
                stream.WriteByte((byte)Math.Min(bytes.Length, 255));
                stream.Write(bytes.AsSpan(0, Math.Min(bytes.Length, 255)));
            }
        });
    }

    public void WriteA(string name, IPAddress address, uint ttl)
    {
        WriteRecord(name, DnsRecordType.A, ttl, () => stream.Write(address.GetAddressBytes()));
    }

    public byte[] ToArray()
    {
        return stream.ToArray();
    }

    private void WriteRecord(string name, DnsRecordType type, uint ttl, Action writePayload)
    {
        WriteName(name);
        WriteUInt16((ushort)type);
        WriteUInt16(1);
        WriteUInt32(ttl);
        var lengthPosition = stream.Position;
        WriteUInt16(0);
        var payloadStart = stream.Position;
        writePayload();
        var payloadLength = checked((ushort)(stream.Position - payloadStart));
        var end = stream.Position;
        stream.Position = lengthPosition;
        WriteUInt16(payloadLength);
        stream.Position = end;
    }

    private void WriteName(string name)
    {
        foreach (var label in name.TrimEnd('.').Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            stream.WriteByte((byte)Math.Min(bytes.Length, 63));
            stream.Write(bytes.AsSpan(0, Math.Min(bytes.Length, 63)));
        }

        stream.WriteByte(0);
    }

    private void WriteUInt16(ushort value)
    {
        Span<byte> buffer = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(buffer, value);
        stream.Write(buffer);
    }

    private void WriteUInt32(uint value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
