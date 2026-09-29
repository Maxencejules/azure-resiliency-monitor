using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace AzureResiliencyMonitor.Core.Monitors;

public sealed class MockServiceMonitor(ILogger<MockServiceMonitor> logger, TimeProvider time) : IServiceMonitor
{
    public ServiceType ServiceType => ServiceType.AppService;
    public Task<HealthCheckResult> CheckHealthAsync(string resourceId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var name = resourceId.Split('/').Last();
        var status = name switch
        {
            "degraded" => HealthStatus.Degraded,
            "unhealthy" or "recovery-fails" => HealthStatus.Unhealthy,
            _ => HealthStatus.Healthy
        };
        return Task.FromResult(new HealthCheckResult
        {
            ServiceName = name, ResourceId = resourceId, ServiceType = ServiceType.AppService,
            Status = status, Message = $"Deterministic mock: {status}",
            CheckedAt = time.GetUtcNow().UtcDateTime, ResponseTime = TimeSpan.FromMilliseconds(120),
            Metadata = new() { ["mock"] = true, ["cpuUsage"] = status == HealthStatus.Healthy ? 35 : 85,
                ["memoryUsage"] = 40, ["requestsPerSecond"] = 100 }
        });
    }
    public Task<bool> AttemptRecoveryAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        logger.LogInformation("Mock recovery requested for {ResourceId}", resourceId);
        return Task.FromResult(!resourceId.EndsWith("/recovery-fails", StringComparison.Ordinal));
    }
}
