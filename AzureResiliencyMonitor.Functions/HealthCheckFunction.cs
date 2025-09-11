using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using AzureResiliencyMonitor.Core.Interfaces;

namespace AzureResiliencyMonitor.Functions;

public class HealthCheckFunction
{
    private readonly ILogger<HealthCheckFunction> _logger;
    private readonly IHealthCheckService _healthCheckService;

    public HealthCheckFunction(
        ILogger<HealthCheckFunction> logger,
        IHealthCheckService healthCheckService)
    {
        _logger = logger;
        _healthCheckService = healthCheckService;
    }

    [Function("HealthCheckTimer")]
    public async Task RunAsync(
        [TimerTrigger("0 */1 * * * *", RunOnStartup = true, UseMonitor = false)] TimerInfo timerInfo)
    {
        _logger.LogInformation("Health check timer triggered at: {time}", DateTime.UtcNow);
        
        try
        {
            var results = await _healthCheckService.CheckAllServicesAsync();
            
            foreach (var result in results)
            {
                _logger.LogInformation(
                    "Service: {Service}, Status: {Status}, ResponseTime: {ResponseTime}ms",
                    result.ServiceName,
                    result.Status,
                    result.ResponseTime.TotalMilliseconds);
            }
            
            if (!results.Any())
            {
                _logger.LogInformation("No services configured for monitoring yet");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during health check execution");
        }
    }
}