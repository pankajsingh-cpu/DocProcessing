using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace DocProcessing.Ingest.Watcher;

public sealed class LandingFolderHealthCheck(IOptions<WatcherOptions> opts) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var path = Path.GetFullPath(opts.Value.LandingPath);
        return Task.FromResult(Directory.Exists(path)
            ? HealthCheckResult.Healthy($"LandingPath exists: {path}")
            : HealthCheckResult.Unhealthy($"LandingPath does not exist: {path}"));
    }
}
