using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QRCoder;
using TennisIntelligence.Connectors;
using TennisIntelligence.Data;
using TennisIntelligence.Models;
using TennisIntelligence.Services;

namespace TennisIntelligence.Pages;

public sealed class ImportsModel : PageModel
{
    private readonly TennisDbContext _db;
    private readonly WearableImportService _importService;
    private readonly InteractionService _interaction;
    private readonly IOptions<ConnectorOptions> _connectorOptions;
    private readonly IWebHostEnvironment _environment;

    public ImportsModel(
        TennisDbContext db,
        WearableImportService importService,
        InteractionService interaction,
        IOptions<ConnectorOptions> connectorOptions,
        IWebHostEnvironment environment)
    {
        _db = db;
        _importService = importService;
        _interaction = interaction;
        _connectorOptions = connectorOptions;
        _environment = environment;
    }

    [BindProperty]
    public IFormFile? Upload { get; set; }

    public List<ImportBatch> RecentImports { get; private set; } = [];
    public List<ExternalWorkout> RecentWorkouts { get; private set; } = [];
    public List<ExternalDailySummary> RecentDailySummaries { get; private set; } = [];
    public List<ExternalBodyMeasurement> RecentBodyMeasurements { get; private set; } = [];
    public WearableImportResult? ImportResult { get; private set; }
    public ImportBatch? LatestConnectorImport { get; private set; }
    public string? PairingUri { get; private set; }
    public string? PairingQrCodeDataUri { get; private set; }
    public string ConnectorEndpoint =>
        $"{Request.Scheme}://{Request.Host}/api/connectors/workouts";

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadRecentDataAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostAsync(CancellationToken cancellationToken)
    {
        if (Upload is null || Upload.Length == 0)
        {
            ModelState.AddModelError(nameof(Upload), "Choose a connector JSON file.");
        }
        else if (Upload.Length > WearableImportService.MaximumFileSizeBytes)
        {
            ModelState.AddModelError(nameof(Upload), "The file must be 10 MB or smaller.");
        }
        else if (!string.Equals(Path.GetExtension(Upload.FileName), ".json", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(Upload), "Only .json connector packages are supported.");
        }

        if (!ModelState.IsValid)
        {
            await LoadRecentDataAsync(cancellationToken);
            return Page();
        }

        var upload = Upload!;
        try
        {
            await using var stream = upload.OpenReadStream();
            ImportResult = await _importService.ImportAsync(stream, upload.FileName, cancellationToken);
            await _interaction.LogAsync(
                PageNames.Imports,
                InteractionActions.WearableDataImported,
                $"inserted:{ImportResult.Batch.InsertedRecords};updated:{ImportResult.Batch.UpdatedRecords};rejected:{ImportResult.Batch.RejectedRecords}");
        }
        catch (WearableImportValidationException ex)
        {
            ModelState.AddModelError(nameof(Upload), ex.Message);
        }

        await LoadRecentDataAsync(cancellationToken);
        return Page();
    }

    private async Task LoadRecentDataAsync(CancellationToken cancellationToken)
    {
        RecentImports = await _db.ImportBatches
            .AsNoTracking()
            .OrderByDescending(b => b.ImportedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        RecentWorkouts = await _db.ExternalWorkouts
            .AsNoTracking()
            .OrderByDescending(w => w.StartedAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        RecentDailySummaries = await _db.ExternalDailySummaries
            .AsNoTracking()
            .OrderByDescending(summary => summary.SummaryDate)
            .Take(14)
            .ToListAsync(cancellationToken);

        RecentBodyMeasurements = await _db.ExternalBodyMeasurements
            .AsNoTracking()
            .OrderByDescending(measurement => measurement.MeasuredAt)
            .Take(10)
            .ToListAsync(cancellationToken);

        LatestConnectorImport = RecentImports
            .FirstOrDefault(batch => batch.Source == "HealthConnect");

        PreparePrivatePairing();
    }

    private void PreparePrivatePairing()
    {
        var apiKey = _connectorOptions.Value.ApiKey;
        if (!_environment.IsDevelopment() || string.IsNullOrWhiteSpace(apiKey))
        {
            return;
        }

        var configuredServerUrl = _connectorOptions.Value.PairingServerUrl?.Trim().TrimEnd('/');
        var serverUrl = !string.IsNullOrWhiteSpace(configuredServerUrl)
            ? configuredServerUrl
            : Request.IsHttps
                && Request.Host.Host.EndsWith(".ts.net", StringComparison.OrdinalIgnoreCase)
                    ? $"{Request.Scheme}://{Request.Host}"
                    : null;
        if (serverUrl is null
            || !Uri.TryCreate(serverUrl, UriKind.Absolute, out var serverUri)
            || serverUri.Scheme != Uri.UriSchemeHttps)
        {
            return;
        }

        PairingUri =
            $"tennisconnector://pair?server={Uri.EscapeDataString(serverUrl)}" +
            $"&key={Uri.EscapeDataString(apiKey)}";

        using var generator = new QRCodeGenerator();
        using var qrData = generator.CreateQrCode(PairingUri, QRCodeGenerator.ECCLevel.Q);
        using var qrCode = new PngByteQRCode(qrData);
        PairingQrCodeDataUri =
            $"data:image/png;base64,{Convert.ToBase64String(qrCode.GetGraphic(8))}";
    }
}
