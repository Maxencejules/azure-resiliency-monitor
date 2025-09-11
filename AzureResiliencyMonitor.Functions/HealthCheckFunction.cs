using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace AzureResiliencyMonitor.Functions;

public class HealthCheckFunction
{
    private readonly ILogger<HealthCheckFunction> _logger;

    public HealthCheckFunction(ILogger<HealthCheckFunction> logger)
    {
        _logger = logger;
    }

    [Function("HealthCheckTimer")]
    public async Task RunAsync([TimerTrigger("0 */5 * * * *")] TimerInfo timerInfo)
    {
        _logger.LogInformation("Health check timer triggered at: {time}", DateTime.UtcNow);
        
        // We'll add the actual health check logic later
        await Task.CompletedTask;
    }
}