using ImageGatewaySupportSimulator.Diagnostics;
using ImageGatewaySupportSimulator.Models;

namespace ImageGatewaySupportSimulator.Services;

// Validates the message, logs each stage, and delegates delivery to IMessageTransport.
// Never sees the image file itself, so it never sets DiagnosticResult.Image - MainForm does.
public class GatewayService
{
    private readonly IMessageTransport _transport;
    private readonly DiagnosticLogger _logger;

    public GatewayService(IMessageTransport transport, DiagnosticLogger logger)
    {
        _transport = transport;
        _logger = logger;
    }

    public async Task<DiagnosticResult> SendImageAsync(ImageMessage message, bool simulateGatewayOffline)
    {
        var result = new DiagnosticResult();

        // Rejected before the request ever reaches the simulated gateway (no delay below has
        // run yet), so Gateway stays NotTested rather than Fail - this is a bad request, not
        // an observed gateway outage.
        var validationError = Validate(message);
        if (validationError is not null)
        {
            result.FailureLayer = "Request Validation";
            result.SuggestedChecks.Add(validationError);
            result.SuggestedChecks.Add("Retry the operation.");
            _logger.Error(validationError, message.CorrelationId);
            return result;
        }

        await Task.Delay(400);

        if (simulateGatewayOffline)
        {
            result.Gateway = DiagnosticStatus.Fail;
            result.FailureLayer = "Local Gateway";
            result.SuggestedChecks.Add("Verify the local gateway/client is running.");
            result.SuggestedChecks.Add("Check workstation configuration/connectivity.");
            result.SuggestedChecks.Add("Retry the operation.");
            _logger.Error("Gateway unavailable", message.CorrelationId);
            return result;
        }

        result.Gateway = DiagnosticStatus.Pass;
        _logger.Info("Gateway accepted request", message.CorrelationId);

        _logger.Info("Message transport sending", message.CorrelationId);
        try
        {
            await _transport.SendAsync(message);
        }
        catch (Exception ex)
        {
            result.MessageTransport = DiagnosticStatus.Fail;
            result.FailureLayer = "Message Transport";
            result.SuggestedChecks.Add("Verify network connectivity.");
            result.SuggestedChecks.Add("Verify message transport availability.");
            result.SuggestedChecks.Add("Retry the operation.");
            _logger.Error($"Message transport unavailable: {ex.Message}", message.CorrelationId);
            return result;
        }

        result.MessageTransport = DiagnosticStatus.Pass;
        result.CloudDestination = DiagnosticStatus.Pass;
        _logger.Success("Cloud destination received", message.CorrelationId);
        _logger.Success("Delivery completed", message.CorrelationId);

        return result;
    }

    private static string? Validate(ImageMessage message)
    {
        if (string.IsNullOrWhiteSpace(message.RecordId))
            return "Record ID is required.";
        if (string.IsNullOrWhiteSpace(message.FileName))
            return "No image selected.";
        if (message.FileSizeBytes <= 0)
            return "Image file could not be read.";

        return null;
    }
}
