using ImageGatewaySupportSimulator.Models;

namespace ImageGatewaySupportSimulator.Services;

// Keeps GatewayService from needing to know which messaging provider is underneath
// (a real version of this could be Kafka, Azure Service Bus, etc.).
public interface IMessageTransport
{
    Task SendAsync(ImageMessage message, CancellationToken cancellationToken = default);
}
