using ImageGatewaySupportSimulator.Models;

namespace ImageGatewaySupportSimulator.Services;

public class MockMessageTransport : IMessageTransport
{
    // Set by MainForm before a send, based on the selected simulation mode.
    public bool SimulateFailure { get; set; }

    public async Task SendAsync(ImageMessage message, CancellationToken cancellationToken = default)
    {
        await Task.Delay(600, cancellationToken);

        if (SimulateFailure)
        {
            throw new InvalidOperationException("Transport endpoint did not respond.");
        }
    }
}
