using StreamDeck.Protocol;
using Xunit;

namespace StreamDeck.Protocol.Tests;

public sealed class HydrationProtocolTests
{
    [Fact]
    public void HelloCarriesTheSameTelemetryAsDeviceInfo()
    {
        var info = new DeviceInfoMessage("1.0.0", 57, new DeviceWifiInfo(true, "lab", -82, "192.168.50.205"),
            "usb", new DeviceStorageInfo(true, true, 64000000000, 59696000000), new DeviceHeapInfo(120000, 100000, 90000));
        var message = ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.Hello, null,
            new HelloMessage("CYD-test", "CYD", "esp32", ["media"], DeviceInfo: info));
        var parsed = ProtocolSerializer.Parse(message);
        Assert.True(parsed.IsSuccess);
        var hello = ProtocolSerializer.DeserializeData<HelloMessage>(parsed.Envelope!);
        Assert.Equal("1.0.0", hello.DeviceInfo!.FirmwareVersion);
        Assert.Equal("192.168.50.205", hello.DeviceInfo.Wifi.Ip);
    }

    [Theory]
    [InlineData(3000000, true)]
    [InlineData(-1, false)]
    public void PerformanceTelemetryValidatesAndRoundTrips(long networkUs, bool accepted)
    {
        var info = new DeviceInfoMessage("1.0.0", 57, new DeviceWifiInfo(true, "lab", -50, "192.168.1.2"),
            "usb", new DeviceStorageInfo(true, true, 10000, 5000), new DeviceHeapInfo(1000, 900, 800),
            Performance: new DevicePerformanceInfo(3001000, 1000, networkUs, 1, 1));
        var parsed = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.DeviceInfo, null, info));
        Assert.Equal(accepted, parsed.IsSuccess);
        if (accepted) Assert.Equal(networkUs, ProtocolSerializer.DeserializeData<DeviceInfoMessage>(parsed.Envelope!).Performance!.NetworkMaxUs);
    }

    [Theory]
    [InlineData("bad", false)]
    [InlineData("cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc", true)]
    public void CoverCacheMissRequestRequiresContentHash(string coverId, bool accepted)
    {
        var parsed = ProtocolSerializer.Parse(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.CoverRequest, null,
            new CoverRequestMessage(coverId)));
        Assert.Equal(accepted, parsed.IsSuccess);
    }

    [Fact]
    public void LegacyHelloWithoutTelemetryAndNewStatusRequestAreAccepted()
    {
        Assert.True(ProtocolSerializer.Parse(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.Hello, null,
            new HelloMessage("CYD-test", "CYD", "esp32", []))).IsSuccess);
        Assert.True(ProtocolSerializer.Parse(ProtocolSerializer.Serialize(ProtocolConstants.MessageTypes.DeviceStatusRequest, null, new { })).IsSuccess);
    }
}
