using System.Diagnostics;

namespace TennisIntelligence.Services;

/// <summary>
/// Self-pings the app every 10 minutes to prevent Render free-tier cold starts.
/// Only active when RENDER_EXTERNAL_URL is set (i.e., deployed on Render).
/// </summary>
internal sealed class KeepAliveService(ILogger<KeepAliveService> logger) : BackgroundService
{
    private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var baseUrl = Environment.GetEnvironmentVariable("RENDER_EXTERNAL_URL");
        if (string.IsNullOrEmpty(baseUrl))
        {
            logger.LogInformation("RENDER_EXTERNAL_URL not set — keep-alive disabled (local dev)");
            return;
        }

        var healthUrl = $"{baseUrl.TrimEnd('/')}/api/health";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        logger.LogInformation("Keep-alive started, pinging {Url} every {Minutes} min", healthUrl, Interval.TotalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            try
            {
                var sw = Stopwatch.StartNew();
                using var response = await http.GetAsync(healthUrl, stoppingToken);
                sw.Stop();
                logger.LogDebug("Keep-alive ping: {Status} in {Ms}ms", response.StatusCode, sw.ElapsedMilliseconds);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Keep-alive ping failed");
            }
        }
    }
}
