namespace ImageGatewaySupportSimulator.Models;

public enum DiagnosticStatus
{
    NotTested,
    Pass,
    Fail
}

// One attempt's outcome across the four layers. FailureLayer is null when everything passes.
public class DiagnosticResult
{
    public DiagnosticStatus Image { get; set; } = DiagnosticStatus.NotTested;
    public DiagnosticStatus Gateway { get; set; } = DiagnosticStatus.NotTested;
    public DiagnosticStatus MessageTransport { get; set; } = DiagnosticStatus.NotTested;
    public DiagnosticStatus CloudDestination { get; set; } = DiagnosticStatus.NotTested;

    public string? FailureLayer { get; set; }
    public List<string> SuggestedChecks { get; } = new();

    public bool Success => FailureLayer is null;
}
