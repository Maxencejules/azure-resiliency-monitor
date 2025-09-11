using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AzureResiliencyMonitor.Core.Monitors;

public class MockServiceMonitor : IServiceMonitor
{
    private readonly ILogger<MockServiceMonitor> _logger;
    private readonly Random _random = new();
    
    public ServiceType ServiceType => ServiceType.AppService;

    public MockServiceMonitor(ILogger<MockServiceMonitor> logger)
    {
        _logger = logger;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        // Simulate network delay
        await Task.Delay(_random.Next(50, 200), cancellationToken);
        
        // Randomly generate health status for demo
        var statuses = new[] { HealthStatus.Healthy, HealthStatus.Healthy, HealthStatus.Healthy, HealthStatus.Degraded, HealthStatus.Unhealthy };
        var status = statuses[_random.Next(statuses.Length)];
        
        var serviceName = resourceId.Split('/').LastOrDefault() ?? "Unknown";
        
        return new HealthCheckResult
        {
            ServiceName = serviceName,
            ResourceId = resourceId,
            ServiceType = DetermineServiceType(resourceId),
            Status = status,
            Message = $"Mock status: {status}",
            CheckedAt = DateTime.UtcNow,
            ResponseTime = stopwatch.Elapsed,
            Metadata = new Dictionary<string, object>
            {
                ["mock"] = true,
                ["cpuUsage"] = _random.Next(10, 90),
                ["memoryUsage"] = _random.Next(20, 80),
                ["requestsPerSecond"] = _random.Next(100, 1000)
            }
        };
    }

    public async Task<bool> AttemptRecoveryAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Mock recovery attempted for {ResourceId}", resourceId);
        await Task.Delay(500, cancellationToken);
        return true;
    }
    
    private ServiceType DetermineServiceType(string resourceId)
    {
        return resourceId switch
        {
            _ when resourceId.Contains("Microsoft.Web") => ServiceType.AppService,
            _ when resourceId.Contains("Microsoft.DocumentDB") => ServiceType.CosmosDB,
            _ when resourceId.Contains("Microsoft.ServiceBus") => ServiceType.ServiceBus,
            _ => ServiceType.AppService
        };
    }
}