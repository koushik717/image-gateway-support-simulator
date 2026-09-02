using System.Text;
using ImageGatewaySupportSimulator.Models;

namespace ImageGatewaySupportSimulator.Diagnostics;

public static class DiagnosticReportExporter
{
    public static string BuildReport(
        string correlationId,
        string simulationMode,
        DiagnosticResult result,
        IEnumerable<LogEntry> timelineEntries)
    {
        var report = new StringBuilder();

        report.AppendLine("IMAGE GATEWAY SUPPORT REPORT");
        report.AppendLine();
        report.AppendLine("Generated:");
        report.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm"));
        report.AppendLine();
        report.AppendLine("Correlation ID:");
        report.AppendLine(correlationId);
        report.AppendLine();
        report.AppendLine("Simulation:");
        report.AppendLine(simulationMode);
        report.AppendLine();

        report.AppendLine("COMPONENT STATUS");
        report.AppendLine();
        report.AppendLine($"Image: {Describe(result.Image)}");
        report.AppendLine($"Gateway: {Describe(result.Gateway)}");
        report.AppendLine($"Message Transport: {Describe(result.MessageTransport)}");
        report.AppendLine($"Cloud Destination: {Describe(result.CloudDestination)}");
        report.AppendLine();

        if (result.FailureLayer is not null)
        {
            report.AppendLine("FAILURE LAYER");
            report.AppendLine();
            report.AppendLine(result.FailureLayer);
        }
        else
        {
            report.AppendLine("RESULT");
            report.AppendLine();
            report.AppendLine("SYSTEM HEALTHY");
        }

        report.AppendLine();
        report.AppendLine("EVENT TIMELINE");
        report.AppendLine();
        foreach (var entry in timelineEntries)
        {
            report.AppendLine($"{entry.Timestamp:HH:mm:ss} {entry.Message}");
        }

        if (result.SuggestedChecks.Count > 0)
        {
            report.AppendLine();
            report.AppendLine("SUGGESTED CHECKS");
            report.AppendLine();
            for (var i = 0; i < result.SuggestedChecks.Count; i++)
            {
                report.AppendLine($"{i + 1}. {result.SuggestedChecks[i]}");
            }
        }

        return report.ToString();
    }

    private static string Describe(DiagnosticStatus status) => status switch
    {
        DiagnosticStatus.Pass => "PASS",
        DiagnosticStatus.Fail => "FAIL",
        _ => "NOT TESTED"
    };
}
