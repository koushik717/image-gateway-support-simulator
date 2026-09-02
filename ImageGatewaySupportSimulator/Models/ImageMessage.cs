namespace ImageGatewaySupportSimulator.Models;

// No image bytes here - only what a real message transport payload would carry.
public class ImageMessage
{
    public required string CorrelationId { get; init; }
    public required string RecordId { get; init; }
    public required string FileName { get; init; }
    public required long FileSizeBytes { get; init; }
    public required DateTime Timestamp { get; init; }
}
