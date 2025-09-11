using AzureResiliencyMonitor.Core.Models;

namespace AzureResiliencyMonitor.Core.Interfaces;

public interface IServiceMonitor
{
    ServiceType ServiceType { get; }
    Task<HealthCheckResult> CheckHealthAsync(string resourceId, CancellationToken cancellationToken = default);
    Task<bool> AttemptRecoveryAsync(string resourceId, CancellationToken cancellationToken = default);
}