using ImageGatewaySupportSimulator.Diagnostics;
using ImageGatewaySupportSimulator.Models;
using ImageGatewaySupportSimulator.Services;

namespace ImageGatewaySupportSimulator.Tests;

// A fake instead of MockMessageTransport, so tests skip the artificial delay and
// can check whether SendAsync was even called.
public class FakeMessageTransport : IMessageTransport
{
    public bool WasCalled { get; private set; }
    public bool ShouldFail { get; init; }

    public Task SendAsync(ImageMessage message, CancellationToken cancellationToken = default)
    {
        WasCalled = true;
        return ShouldFail
            ? throw new InvalidOperationException("Simulated transport failure.")
            : Task.CompletedTask;
    }
}

public class GatewayServiceTests
{
    private static ImageMessage CreateMessage(string recordId = "DEMO-001", string fileName = "demo.png", long sizeBytes = 1024) =>
        new()
        {
            CorrelationId = "TEST01",
            RecordId = recordId,
            FileName = fileName,
            FileSizeBytes = sizeBytes,
            Timestamp = DateTime.Now
        };

    [Fact]
    public async Task Healthy_message_is_delivered_successfully()
    {
        var transport = new FakeMessageTransport();
        var gateway = new GatewayService(transport, new DiagnosticLogger());

        var result = await gateway.SendImageAsync(CreateMessage(), simulateGatewayOffline: false);

        Assert.True(result.Success);
        Assert.Equal(DiagnosticStatus.Pass, result.Gateway);
        Assert.Equal(DiagnosticStatus.Pass, result.MessageTransport);
        Assert.Equal(DiagnosticStatus.Pass, result.CloudDestination);
        Assert.True(transport.WasCalled);
    }

    [Fact]
    public async Task Missing_record_id_is_rejected_as_a_validation_error()
    {
        var transport = new FakeMessageTransport();
        var gateway = new GatewayService(transport, new DiagnosticLogger());

        var result = await gateway.SendImageAsync(CreateMessage(recordId: ""), simulateGatewayOffline: false);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticStatus.NotTested, result.Gateway);
        Assert.Equal("Request Validation", result.FailureLayer);
        Assert.False(transport.WasCalled);
    }

    [Fact]
    public async Task Missing_file_name_is_rejected_as_a_validation_error()
    {
        var transport = new FakeMessageTransport();
        var gateway = new GatewayService(transport, new DiagnosticLogger());

        var result = await gateway.SendImageAsync(CreateMessage(fileName: ""), simulateGatewayOffline: false);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticStatus.NotTested, result.Gateway);
        Assert.Equal("Request Validation", result.FailureLayer);
        Assert.False(transport.WasCalled);
    }

    [Fact]
    public async Task Gateway_offline_fails_before_reaching_transport()
    {
        var transport = new FakeMessageTransport();
        var gateway = new GatewayService(transport, new DiagnosticLogger());

        var result = await gateway.SendImageAsync(CreateMessage(), simulateGatewayOffline: true);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticStatus.Fail, result.Gateway);
        Assert.Equal("Local Gateway", result.FailureLayer);
        Assert.Equal(DiagnosticStatus.NotTested, result.MessageTransport);
        Assert.False(transport.WasCalled);
    }

    [Fact]
    public async Task Transport_failure_is_isolated_to_the_transport_layer()
    {
        var transport = new FakeMessageTransport { ShouldFail = true };
        var gateway = new GatewayService(transport, new DiagnosticLogger());

        var result = await gateway.SendImageAsync(CreateMessage(), simulateGatewayOffline: false);

        Assert.False(result.Success);
        Assert.Equal(DiagnosticStatus.Pass, result.Gateway);
        Assert.Equal(DiagnosticStatus.Fail, result.MessageTransport);
        Assert.Equal(DiagnosticStatus.NotTested, result.CloudDestination);
        Assert.Equal("Message Transport", result.FailureLayer);
    }
}
