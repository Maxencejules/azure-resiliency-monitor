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
        _monitoredResources = new List<string>();
        
        // Load from configuration
        var resourcesConfig = Environment.GetEnvironmentVariable("MonitoredResources");
        if (!string.IsNullOrEmpty(resourcesConfig))
        {
            var resources = resourcesConfig.Split(',', StringSplitOptions.RemoveEmptyEntries);
            _monitoredResources.AddRange(resources);
            _logger.LogInformation("Loaded {Count} resources from configuration", resources.Length);
        }
        
        // Add mock resources if in development mode
        var isDevelopment = Environment.GetEnvironmentVariable("AZURE_FUNCTIONS_ENVIRONMENT") == "Development";
        if (isDevelopment && !_monitoredResources.Any())
        {
            _monitoredResources.AddRange(new[]
            {
                "mock://subscription/test-sub/resourceGroups/test-rg/providers/Microsoft.Web/sites/app-1",
                "mock://subscription/test-sub/resourceGroups/test-rg/providers/Microsoft.Web/sites/app-2",
                "mock://subscription/test-sub/resourceGroups/test-rg/providers/Microsoft.DocumentDB/databaseAccounts/cosmos-1"
            });
            _logger.LogInformation("Added {Count} mock resources for development", _monitoredResources.Count);
        }
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
        // For mock resources, always use the first registered monitor
        if (resourceId.StartsWith("mock://"))
        {
            var monitor = _monitors.Values.FirstOrDefault();
            if (monitor != null)
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
        }
        
        // Original code for real resources
        var serviceType = DetermineServiceType(resourceId);
        
        if (_monitors.TryGetValue(serviceType, out var realMonitor))
        {
            var result = await realMonitor.CheckHealthAsync(resourceId, cancellationToken);
            
            if (result.Status == HealthStatus.Unhealthy)
            {
                _logger.LogWarning("Service {ResourceId} is unhealthy, attempting recovery", resourceId);
                var recovered = await realMonitor.AttemptRecoveryAsync(resourceId, cancellationToken);
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