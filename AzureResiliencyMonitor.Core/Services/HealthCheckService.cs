using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using Microsoft.Extensions.Logging;

namespace AzureResiliencyMonitor.Core.Services;

public class HealthCheckService : IHealthCheckService
{
    private readonly Dictionary<ServiceType, IServiceMonitor> _monitors = new();
    private readonly ILogger<HealthCheckService> _logger;
    private readonly List<string> _monitoredResources;

    public HealthCheckService(ILogger<HealthCheckService> logger)
    {
        _logger = logger;
        
        // TODO: Load from configuration
        _monitoredResources = new List<string>
        {
            // Add your actual Azure resource IDs here
            // Format: /subscriptions/{id}/resourceGroups/{rg}/providers/Microsoft.Web/sites/{name}
        };
    }

    public void RegisterMonitor(IServiceMonitor monitor)
    {
        _monitors[monitor.ServiceType] = monitor;
        _logger.LogInformation("Registered monitor for {ServiceType}", monitor.ServiceType);
    }

    public async Task<IEnumerable<HealthCheckResult>> CheckAllServicesAsync(CancellationToken cancellationToken = default)
    {
        if (!_monitoredResources.Any())
        {
            _logger.LogWarning("No resources configured for monitoring");
            return Array.Empty<HealthCheckResult>();
        }

        var tasks = _monitoredResources.Select(resourceId => 
            CheckServiceAsync(resourceId, cancellationToken));
        
        return await Task.WhenAll(tasks);
    }

    public async Task<HealthCheckResult> CheckServiceAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var serviceType = DetermineServiceType(resourceId);
        
        if (_monitors.TryGetValue(serviceType, out var monitor))
        {
            var result = await monitor.CheckHealthAsync(resourceId, cancellationToken);
            
            if (result.Status == HealthStatus.Unhealthy)
            {
                _logger.LogWarning("Service {ResourceId} is unhealthy, attempting recovery", resourceId);
                var recovered = await monitor.AttemptRecoveryAsync(resourceId, cancellationToken);
                result.Metadata["recoveryAttempted"] = recovered;
            }
            
            return result;
        }

        return new HealthCheckResult
        {
            ResourceId = resourceId,
            Status = HealthStatus.Unknown,
            Message = "No monitor registered for this service type",
            CheckedAt = DateTime.UtcNow
        };
    }

    private ServiceType DetermineServiceType(string resourceId)
    {
        return resourceId switch
        {
            _ when resourceId.Contains("/Microsoft.Web/sites") => ServiceType.AppService,
            _ when resourceId.Contains("/Microsoft.DocumentDB/databaseAccounts") => ServiceType.CosmosDB,
            _ when resourceId.Contains("/Microsoft.ServiceBus/namespaces") => ServiceType.ServiceBus,
            _ when resourceId.Contains("/Microsoft.Storage/storageAccounts") => ServiceType.StorageAccount,
            _ => ServiceType.AppService
        };
    }
}