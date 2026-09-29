namespace AzureResiliencyMonitor.Core.Models;

public class HealthCheckResult
{
    public string ServiceName { get; set; } = string.Empty;
    public string ResourceId { get; set; } = string.Empty;
    public ServiceType ServiceType { get; set; }
    public HealthStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime CheckedAt { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
    public TimeSpan ResponseTime { get; set; }
    public double ResponseTimeMs => ResponseTime.TotalMilliseconds;
}
public enum ServiceType { AppService, FunctionApp, CosmosDB, ServiceBus, StorageAccount }
public enum HealthStatus { Healthy, Degraded, Unhealthy, Unknown }
