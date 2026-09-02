using ImageGatewaySupportSimulator.Diagnostics;
using ImageGatewaySupportSimulator.Models;
using ImageGatewaySupportSimulator.Services;

namespace ImageGatewaySupportSimulator.Forms;

public class MainForm : Form
{
    private const string HealthyMode = "Healthy";
    private const string GatewayOfflineMode = "Gateway Offline";
    private const string TransportFailureMode = "Message Transport Failure";

    private readonly DiagnosticLogger _logger = new();
    private readonly MockMessageTransport _messageTransport = new();
    private readonly GatewayService _gatewayService;
    private readonly List<LogEntry> _timeline = new();

    private string? _selectedImagePath;
    private long _selectedImageSizeBytes;

    private DiagnosticResult? _lastResult;
    private string? _lastCorrelationId;
    private string? _lastSimulationMode;

    // Controls
    private TextBox txtRecordId = null!;
    private Button btnSelectImage = null!;
    private Label lblFileName = null!;
    private Label lblFileSize = null!;
    private PictureBox picPreview = null!;
    private ComboBox cmbSimulationMode = null!;
    private Button btnSendImage = null!;
    private Button btnRetry = null!;

    private Label lblImageStatus = null!;
    private Label lblGatewayStatus = null!;
    private Label lblTransportStatus = null!;
    private Label lblCloudStatus = null!;
    private Label lblCorrelationId = null!;
    private Label lblDiagnosticsSummary = null!;
    private Label lblSuggestedChecksHeader = null!;
    private Label lblSuggestedChecksBody = null!;
    private Button btnExportReport = null!;

    private TextBox txtTimeline = null!;

    public MainForm()
    {
        _gatewayService = new GatewayService(_messageTransport, _logger);

        InitializeUi();

        _logger.EntryLogged += Logger_EntryLogged;
        cmbSimulationMode.SelectedIndex = 0;
        UpdateDiagnosticsPanel(new DiagnosticResult());
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            picPreview.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void Logger_EntryLogged(object? sender, LogEntry entry)
    {
        _timeline.Add(entry);
        txtTimeline.AppendText(entry + Environment.NewLine);
    }

    private void btnSelectImage_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Image files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
            Title = "Select a synthetic sample image"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        ResetAttemptState();

        try
        {
            var fileInfo = new FileInfo(dialog.FileName);
            var preview = LoadPreviewWithoutLockingFile(dialog.FileName);

            picPreview.Image?.Dispose();
            picPreview.Image = preview;

            _selectedImagePath = dialog.FileName;
            _selectedImageSizeBytes = fileInfo.Length;

            lblFileName.Text = fileInfo.Name;
            lblFileSize.Text = $"File size: {FormatFileSize(fileInfo.Length)}";

            _logger.Info($"Image selected: {fileInfo.Name}");
        }
        catch (Exception ex)
        {
            _selectedImagePath = null;
            _selectedImageSizeBytes = 0;
            MessageBox.Show(this, "That file could not be read as an image. Please choose a different file.",
                "Unsupported image", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            _logger.Error($"Selected file could not be read as an image: {ex.Message}");
        }
    }

    // Selecting a new image invalidates whatever the diagnostics panel and Export
    // button were showing for the previous image - otherwise Export could save a
    // report for an attempt that no longer matches what's on screen.
    private void ResetAttemptState()
    {
        btnRetry.Visible = false;
        btnExportReport.Enabled = false;
        _lastResult = null;
        _lastCorrelationId = null;
        _lastSimulationMode = null;
        lblCorrelationId.Text = "Correlation ID: -";
        UpdateDiagnosticsPanel(new DiagnosticResult());
    }

    // Image.FromFile keeps the source file locked for as long as the Image is in use.
    // Reading the bytes first and building an independent Bitmap avoids that lock.
    private static Bitmap LoadPreviewWithoutLockingFile(string path)
    {
        var fileBytes = File.ReadAllBytes(path);
        using var memoryStream = new MemoryStream(fileBytes);
        using var loadedImage = Image.FromStream(memoryStream);
        return new Bitmap(loadedImage);
    }

    private async void btnSendImage_Click(object? sender, EventArgs e)
    {
        await RunDeliveryAsync();
    }

    private async void btnRetry_Click(object? sender, EventArgs e)
    {
        btnRetry.Visible = false;
        await RunDeliveryAsync();
    }

    private async Task RunDeliveryAsync()
    {
        if (string.IsNullOrWhiteSpace(txtRecordId.Text))
        {
            MessageBox.Show(this, "Please enter a Record ID.", "Record ID required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_selectedImagePath is null)
        {
            MessageBox.Show(this, "Please select an image before sending.", "No image selected",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        btnSendImage.Enabled = false;
        btnSelectImage.Enabled = false;
        ResetAttemptState();

        var correlationId = GenerateCorrelationId();
        lblCorrelationId.Text = $"Correlation ID: {correlationId}";
        _logger.Info("Delivery requested", correlationId);

        try
        {
            var result = await ValidateImageAndSendAsync(correlationId);

            _lastResult = result;
            _lastCorrelationId = correlationId;
            _lastSimulationMode = cmbSimulationMode.SelectedItem as string ?? HealthyMode;
            btnExportReport.Enabled = true;

            UpdateDiagnosticsPanel(result);
            btnRetry.Visible = !result.Success;
        }
        finally
        {
            btnSendImage.Enabled = true;
            btnSelectImage.Enabled = true;
        }
    }

    private async Task<DiagnosticResult> ValidateImageAndSendAsync(string correlationId)
    {
        try
        {
            using var loaded = LoadPreviewWithoutLockingFile(_selectedImagePath!);
        }
        catch (Exception ex)
        {
            _logger.Error($"Image could not be validated: {ex.Message}", correlationId);

            var imageFailure = new DiagnosticResult { Image = DiagnosticStatus.Fail, FailureLayer = "Image" };
            imageFailure.SuggestedChecks.Add("Verify the image file still exists.");
            imageFailure.SuggestedChecks.Add("Reselect the image and try again.");
            return imageFailure;
        }

        _logger.Info("Image validated", correlationId);

        var message = new ImageMessage
        {
            CorrelationId = correlationId,
            RecordId = txtRecordId.Text.Trim(),
            FileName = Path.GetFileName(_selectedImagePath!),
            FileSizeBytes = _selectedImageSizeBytes,
            Timestamp = DateTime.Now
        };

        var simulationMode = cmbSimulationMode.SelectedItem as string ?? HealthyMode;
        _messageTransport.SimulateFailure = simulationMode == TransportFailureMode;
        var simulateGatewayOffline = simulationMode == GatewayOfflineMode;

        var result = await _gatewayService.SendImageAsync(message, simulateGatewayOffline);
        result.Image = DiagnosticStatus.Pass;
        return result;
    }

    private void UpdateDiagnosticsPanel(DiagnosticResult result)
    {
        SetStatusLabel(lblImageStatus, result.Image);
        SetStatusLabel(lblGatewayStatus, result.Gateway);
        SetStatusLabel(lblTransportStatus, result.MessageTransport);
        SetStatusLabel(lblCloudStatus, result.CloudDestination);

        if (result.FailureLayer is not null)
        {
            lblDiagnosticsSummary.Text = $"Failure isolated to:\n{result.FailureLayer.ToUpperInvariant()}";
            lblDiagnosticsSummary.ForeColor = Color.Firebrick;
        }
        else if (result.Image == DiagnosticStatus.NotTested)
        {
            lblDiagnosticsSummary.Text = string.Empty;
            lblDiagnosticsSummary.ForeColor = SystemColors.ControlText;
        }
        else
        {
            lblDiagnosticsSummary.Text = "SYSTEM HEALTHY";
            lblDiagnosticsSummary.ForeColor = Color.SeaGreen;
        }

        lblSuggestedChecksHeader.Visible = result.SuggestedChecks.Count > 0;
        lblSuggestedChecksBody.Text = result.SuggestedChecks.Count > 0
            ? string.Join(Environment.NewLine,
                result.SuggestedChecks.Select((check, index) => $"{index + 1}. {check}"))
            : string.Empty;
    }

    private static void SetStatusLabel(Label label, DiagnosticStatus status)
    {
        label.Text = status switch
        {
            DiagnosticStatus.Pass => "PASS",
            DiagnosticStatus.Fail => "FAIL",
            _ => "NOT TESTED"
        };
        label.ForeColor = status switch
        {
            DiagnosticStatus.Pass => Color.SeaGreen,
            DiagnosticStatus.Fail => Color.Firebrick,
            _ => Color.Gray
        };
    }

    private void btnExportReport_Click(object? sender, EventArgs e)
    {
        if (_lastResult is null || _lastCorrelationId is null)
        {
            return;
        }

        using var dialog = new SaveFileDialog
        {
            Filter = "Text file (*.txt)|*.txt",
            FileName = $"gateway-support-report-{_lastCorrelationId}.txt"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var attemptEntries = _timeline.Where(entry => entry.CorrelationId == _lastCorrelationId);
            var reportText = DiagnosticReportExporter.BuildReport(
                _lastCorrelationId, _lastSimulationMode ?? HealthyMode, _lastResult, attemptEntries);

            File.WriteAllText(dialog.FileName, reportText);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"The report could not be saved: {ex.Message}", "Export failed",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static string GenerateCorrelationId() =>
        Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();

    private static string FormatFileSize(long bytes)
    {
        const long kb = 1024;
        const long mb = kb * 1024;

        return bytes switch
        {
            < kb => $"{bytes} B",
            < mb => $"{bytes / (double)kb:0.0} KB",
            _ => $"{bytes / (double)mb:0.0} MB"
        };
    }

    private void InitializeUi()
    {
        Text = "Image Gateway Support Simulator";
        ClientSize = new Size(1000, 800);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        var lblTitle = new Label
        {
            Text = "Image Gateway Support Simulator",
            Location = new Point(16, 14),
            AutoSize = true,
            Font = new Font("Segoe UI", 16F, FontStyle.Bold)
        };

        var lblSubtitle = new Label
        {
            Text = "WinForms Learning POC",
            Location = new Point(16, 46),
            AutoSize = true,
            ForeColor = Color.Gray
        };

        var grpDelivery = BuildDeliveryGroup();
        var grpDiagnostics = BuildDiagnosticsGroup();
        var grpTimeline = BuildTimelineGroup();

        Controls.AddRange(new Control[] { lblTitle, lblSubtitle, grpDelivery, grpDiagnostics, grpTimeline });
    }

    private GroupBox BuildDeliveryGroup()
    {
        var group = new GroupBox
        {
            Text = "Image Delivery",
            Location = new Point(16, 78),
            Size = new Size(470, 400)
        };

        var lblRecordId = new Label { Text = "Record ID:", Location = new Point(12, 28), AutoSize = true };
        txtRecordId = new TextBox { Text = "DEMO-001", Location = new Point(110, 24), Width = 160 };

        btnSelectImage = new Button
        {
            Text = "Select Image",
            Location = new Point(12, 60),
            Size = new Size(130, 30)
        };
        btnSelectImage.Click += btnSelectImage_Click;

        lblFileName = new Label
        {
            Text = "No image selected",
            Location = new Point(12, 100),
            Size = new Size(430, 20),
            AutoEllipsis = true
        };

        picPreview = new PictureBox
        {
            Location = new Point(12, 124),
            Size = new Size(170, 170),
            BorderStyle = BorderStyle.FixedSingle,
            SizeMode = PictureBoxSizeMode.Zoom
        };

        lblFileSize = new Label
        {
            Text = "File size: -",
            Location = new Point(192, 124),
            AutoSize = true
        };

        var lblSimulationMode = new Label
        {
            Text = "Simulation Mode:",
            Location = new Point(12, 316),
            AutoSize = true
        };

        cmbSimulationMode = new ComboBox
        {
            Location = new Point(150, 312),
            Width = 300,
            DropDownStyle = ComboBoxStyle.DropDownList
        };
        cmbSimulationMode.Items.AddRange(new object[] { HealthyMode, GatewayOfflineMode, TransportFailureMode });

        btnSendImage = new Button
        {
            Text = "Send Image",
            Location = new Point(12, 354),
            Size = new Size(140, 32)
        };
        btnSendImage.Click += btnSendImage_Click;

        btnRetry = new Button
        {
            Text = "Retry",
            Location = new Point(162, 354),
            Size = new Size(90, 32),
            Visible = false
        };
        btnRetry.Click += btnRetry_Click;

        group.Controls.AddRange(new Control[]
        {
            lblRecordId, txtRecordId, btnSelectImage, lblFileName, picPreview, lblFileSize,
            lblSimulationMode, cmbSimulationMode, btnSendImage, btnRetry
        });

        return group;
    }

    private GroupBox BuildDiagnosticsGroup()
    {
        var group = new GroupBox
        {
            Text = "Support Diagnostics",
            Location = new Point(502, 78),
            Size = new Size(482, 400)
        };

        const int labelX = 12;
        const int statusX = 250;
        var statusFont = new Font("Segoe UI", 9F, FontStyle.Bold);

        var lblImageName = new Label { Text = "Image", Location = new Point(labelX, 26), AutoSize = true };
        lblImageStatus = new Label { Location = new Point(statusX, 26), AutoSize = true, Font = statusFont };

        var lblGatewayName = new Label { Text = "Gateway", Location = new Point(labelX, 50), AutoSize = true };
        lblGatewayStatus = new Label { Location = new Point(statusX, 50), AutoSize = true, Font = statusFont };

        var lblTransportName = new Label { Text = "Message Transport", Location = new Point(labelX, 74), AutoSize = true };
        lblTransportStatus = new Label { Location = new Point(statusX, 74), AutoSize = true, Font = statusFont };

        var lblCloudName = new Label { Text = "Cloud Destination", Location = new Point(labelX, 98), AutoSize = true };
        lblCloudStatus = new Label { Location = new Point(statusX, 98), AutoSize = true, Font = statusFont };

        lblCorrelationId = new Label
        {
            Text = "Correlation ID: -",
            Location = new Point(labelX, 134),
            AutoSize = true,
            Font = statusFont
        };

        lblDiagnosticsSummary = new Label
        {
            Location = new Point(labelX, 164),
            Size = new Size(450, 40),
            Font = new Font("Segoe UI", 10F, FontStyle.Bold)
        };

        lblSuggestedChecksHeader = new Label
        {
            Text = "Suggested checks:",
            Location = new Point(labelX, 214),
            AutoSize = true,
            Visible = false
        };

        lblSuggestedChecksBody = new Label
        {
            Location = new Point(labelX, 236),
            Size = new Size(450, 90)
        };

        btnExportReport = new Button
        {
            Text = "Export Diagnostic Report",
            Location = new Point(labelX, 340),
            Size = new Size(230, 32),
            Enabled = false
        };
        btnExportReport.Click += btnExportReport_Click;

        group.Controls.AddRange(new Control[]
        {
            lblImageName, lblImageStatus, lblGatewayName, lblGatewayStatus,
            lblTransportName, lblTransportStatus, lblCloudName, lblCloudStatus,
            lblCorrelationId, lblDiagnosticsSummary, lblSuggestedChecksHeader, lblSuggestedChecksBody,
            btnExportReport
        });

        return group;
    }

    private GroupBox BuildTimelineGroup()
    {
        var group = new GroupBox
        {
            Text = "Event Timeline",
            Location = new Point(16, 494),
            Size = new Size(968, 254)
        };

        txtTimeline = new TextBox
        {
            Location = new Point(12, 22),
            Size = new Size(944, 220),
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            Font = new Font("Consolas", 9F)
        };
        group.Controls.Add(txtTimeline);

        return group;
    }
}
