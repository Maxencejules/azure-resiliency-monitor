using Azure;
using Azure.Core;
using Azure.Identity;
using Azure.ResourceManager;
using Azure.ResourceManager.AppService;
using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace AzureResiliencyMonitor.Core.Monitors;

public class AppServiceMonitor : IServiceMonitor
{
    private readonly ArmClient _armClient;
    private readonly ILogger<AppServiceMonitor> _logger;
    
    public ServiceType ServiceType => ServiceType.AppService;

    public AppServiceMonitor(ILogger<AppServiceMonitor> logger)
        : this(logger, new ArmClient(new DefaultAzureCredential()))
    {
    }

    public AppServiceMonitor(ILogger<AppServiceMonitor> logger, ArmClient armClient)
    {
        _logger = logger;
        _armClient = armClient ?? throw new ArgumentNullException(nameof(armClient));
    }

    public async Task<HealthCheckResult> CheckHealthAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = new HealthCheckResult
        {
            ServiceName = "App Service",
            ResourceId = resourceId,
            ServiceType = ServiceType.AppService,
            CheckedAt = DateTime.UtcNow
        };

        try
        {
            var resource = _armClient.GetWebSiteResource(new ResourceIdentifier(resourceId));
            var webSite = await resource.GetAsync(cancellationToken);
            
            result.Status = webSite.Value.Data.State == "Running" 
                ? HealthStatus.Healthy 
                : HealthStatus.Unhealthy;
                
            result.Message = $"App Service is {webSite.Value.Data.State}";
            result.Metadata["state"] = webSite.Value.Data.State ?? "Unknown";
            
            _logger.LogInformation("Health check completed for {ResourceId}: {Status}", 
                resourceId, result.Status);
        }
        catch (RequestFailedException ex)
        {
            result.Status = HealthStatus.Unknown;
            result.Message = $"Failed to check health: {ex.Message}";
            _logger.LogError(ex, "Error checking health for {ResourceId}", resourceId);
        }
        finally
        {
            result.ResponseTime = stopwatch.Elapsed;
        }

        return result;
    }

    public async Task<bool> AttemptRecoveryAsync(string resourceId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Attempting recovery for {ResourceId}", resourceId);
            
            var resource = _armClient.GetWebSiteResource(new ResourceIdentifier(resourceId));
            var webSite = await resource.GetAsync(cancellationToken);
            
            if (webSite.Value.Data.State != "Running")
            {
                await resource.RestartAsync(softRestart: true, synchronous: false, cancellationToken);
                _logger.LogInformation("Restart initiated for {ResourceId}", resourceId);
                return true;
            }
            
            return false;
        }
        catch (Exception ex)
        {
            if (ex is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw;
            _logger.LogError(ex, "Recovery failed for {ResourceId}", resourceId);
            return false;
        }
    }
}
