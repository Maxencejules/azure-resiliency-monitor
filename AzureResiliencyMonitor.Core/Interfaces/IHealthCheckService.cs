using AzureResiliencyMonitor.Core.Models;

namespace AzureResiliencyMonitor.Core.Interfaces;

public interface IHealthCheckService
{
    IReadOnlyList<HealthCheckResult> GetCurrentHealth();
    Task<IEnumerable<HealthCheckResult>> CheckAllServicesAsync(CancellationToken cancellationToken = default);
    Task<HealthCheckResult> CheckServiceAsync(string resourceId, CancellationToken cancellationToken = default);
}
