using System.Buffers;
using System.IO.Ports;

namespace StreamDeck.WindowsAgent.Runtime;

public sealed class SerialPortUsbDuplexStream : IUsbDuplexStream
{
    public const int BaudRate = 115200;

    private readonly SerialPort serialPort;
    private readonly Stream stream;

    public SerialPortUsbDuplexStream(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("USB port is required.", nameof(portName));

        serialPort = new SerialPort(portName, BaudRate)
        {
            DtrEnable = false,
            RtsEnable = false,
            NewLine = "\n",
            ReadTimeout = 500,
            WriteTimeout = 2000
        };
        serialPort.Open();
        stream = serialPort.BaseStream;
    }

    public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            if (!serialPort.IsOpen) return 0;
            var available = serialPort.BytesToRead;
            if (available > 0)
            {
                var array = ArrayPool<byte>.Shared.Rent(buffer.Length);
                try
                {
                    var toRead = Math.Min(buffer.Length, available);
                    var count = serialPort.Read(array, 0, toRead);
                    if (count > 0)
                    {
                        new ReadOnlySpan<byte>(array, 0, count).CopyTo(buffer.Span);
                        return count;
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(array);
                }
            }

            await Task.Delay(10, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();
        return 0;
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (buffer.Length == 0) return;
        var array = buffer.ToArray();
        serialPort.Write(array, 0, array.Length);
        await Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            if (serialPort.IsOpen)
            {
                serialPort.Close();
            }
            stream.Dispose();
            serialPort.Dispose();
        }
        catch
        {
        }
        return ValueTask.CompletedTask;
    }
}
