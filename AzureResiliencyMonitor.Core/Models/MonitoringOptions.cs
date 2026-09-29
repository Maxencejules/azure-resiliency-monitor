namespace AzureResiliencyMonitor.Core.Models;

public sealed class MonitoringOptions
{
    public string[] Resources { get; set; } = [];
    public bool RecoveryEnabled { get; set; }
    public TimeSpan RecoveryCooldown { get; set; } = TimeSpan.FromMinutes(5);
}
