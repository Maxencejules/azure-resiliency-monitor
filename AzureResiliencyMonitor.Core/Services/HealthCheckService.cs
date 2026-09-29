using System.Collections.Concurrent;
using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AzureResiliencyMonitor.Core.Services;

public sealed class HealthCheckService : IHealthCheckService
{
    private readonly ILogger<HealthCheckService> _logger;
    private readonly IReadOnlyDictionary<ServiceType, IServiceMonitor> _monitors;
    private readonly MonitoringOptions _options;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _checkLock = new(1, 1);
    private readonly ConcurrentDictionary<string, HealthCheckResult> _snapshots = new(StringComparer.OrdinalIgnoreCase);
    // Check methods hold _checkLock for both reservation and resource I/O.
    private readonly Dictionary<string, DateTimeOffset> _lastRecoveryAttempts = new(StringComparer.OrdinalIgnoreCase);

    public HealthCheckService(ILogger<HealthCheckService> logger,
        IEnumerable<IServiceMonitor> monitors, IOptions<MonitoringOptions> options, TimeProvider time)
    {
        _logger = logger;
        _monitors = monitors.ToDictionary(m => m.ServiceType);
        _options = options.Value;
        _time = time;
        if (_options.RecoveryCooldown <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(options), "Recovery cooldown must be positive.");
    }

    public IReadOnlyList<HealthCheckResult> GetCurrentHealth() =>
        _snapshots.Values.OrderBy(r => r.ResourceId).Select(Copy).ToArray();

    public async Task<IEnumerable<HealthCheckResult>> CheckAllServicesAsync(
        CancellationToken cancellationToken = default)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try
        {
            foreach (var resource in _options.Resources.Distinct(StringComparer.OrdinalIgnoreCase))
                await CheckAndRecordAsync(resource, cancellationToken);
            return GetCurrentHealth();
        }
        finally { _checkLock.Release(); }
    }

    public async Task<HealthCheckResult> CheckServiceAsync(
        string resourceId, CancellationToken cancellationToken = default)
    {
        await _checkLock.WaitAsync(cancellationToken);
        try { return Copy(await CheckAndRecordAsync(resourceId, cancellationToken)); }
        finally { _checkLock.Release(); }
    }

    private async Task<HealthCheckResult> CheckAndRecordAsync(string resourceId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var type = DetermineServiceType(resourceId);
        HealthCheckResult result;
        if (type is null || !_monitors.TryGetValue(type.Value, out var monitor))
        {
            result = new HealthCheckResult
            {
                ResourceId = resourceId,
                ServiceName = resourceId.Split('/').Last(),
                ServiceType = type ?? ServiceType.AppService,
                Status = HealthStatus.Unknown,
                Message = "No monitor registered for this resource type."
            };
            SetRecovery(result, "Unsupported");
        }
        else
        {
            try
            {
                result = await monitor.CheckHealthAsync(resourceId, cancellationToken);
                await ApplyRecoveryPolicyAsync(result, monitor, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed for {ResourceId}", resourceId);
                result = new HealthCheckResult
                {
                    ResourceId = resourceId,
                    ServiceName = resourceId.Split('/').Last(),
                    ServiceType = type.Value,
                    Status = HealthStatus.Unknown,
                    Message = "Health check failed.",
                    Metadata = new() { ["checkError"] = ex.Message }
                };
                SetRecovery(result, "NotNeeded");
            }
        }
        result.CheckedAt = _time.GetUtcNow().UtcDateTime;
        _snapshots[resourceId] = Copy(result);
        return result;
    }

    private async Task ApplyRecoveryPolicyAsync(HealthCheckResult result, IServiceMonitor monitor,
        CancellationToken cancellationToken)
    {
        SetRecovery(result, "NotNeeded");
        if (result.Status != HealthStatus.Unhealthy) return;
        if (!_options.RecoveryEnabled) { SetRecovery(result, "Disabled"); return; }
        var now = _time.GetUtcNow();
        if (_lastRecoveryAttempts.TryGetValue(result.ResourceId, out var lastAttempt) &&
            now < lastAttempt + _options.RecoveryCooldown)
        {
            SetRecovery(result, "Cooldown");
            result.Metadata["nextRecoveryAt"] = lastAttempt + _options.RecoveryCooldown;
            return;
        }
        // Failed attempts also consume cooldown so failures cannot cause restart storms.
        _lastRecoveryAttempts[result.ResourceId] = now;
        result.Metadata["recoveryAttemptedAt"] = now;
        result.Metadata["nextRecoveryAt"] = now + _options.RecoveryCooldown;
        try
        {
            var accepted = await monitor.AttemptRecoveryAsync(result.ResourceId, cancellationToken);
            SetRecovery(result, accepted ? "Started" : "Failed", attempted: true, succeeded: accepted);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Recovery failed for {ResourceId}", result.ResourceId);
            SetRecovery(result, "Failed", attempted: true);
            result.Metadata["recoveryError"] = ex.Message;
        }
    }

    private static void SetRecovery(HealthCheckResult result, string outcome,
        bool attempted = false, bool succeeded = false)
    {
        result.Metadata["recoveryOutcome"] = outcome;
        result.Metadata["recoveryAttempted"] = attempted;
        // Accepted restart request; only later checks can establish recovered health.
        result.Metadata["recoverySucceeded"] = succeeded;
    }

    private static ServiceType? DetermineServiceType(string resourceId) => resourceId switch
    {
        _ when resourceId.StartsWith("mock://", StringComparison.OrdinalIgnoreCase) => ServiceType.AppService,
        _ when resourceId.Contains("/providers/Microsoft.Web/sites/", StringComparison.OrdinalIgnoreCase) => ServiceType.AppService,
        _ when resourceId.Contains("/providers/Microsoft.DocumentDB/databaseAccounts/", StringComparison.OrdinalIgnoreCase) => ServiceType.CosmosDB,
        _ when resourceId.Contains("/providers/Microsoft.ServiceBus/namespaces/", StringComparison.OrdinalIgnoreCase) => ServiceType.ServiceBus,
        _ when resourceId.Contains("/providers/Microsoft.Storage/storageAccounts/", StringComparison.OrdinalIgnoreCase) => ServiceType.StorageAccount,
        _ => null
    };

    private static HealthCheckResult Copy(HealthCheckResult result) => new()
    {
        ServiceName = result.ServiceName, ResourceId = result.ResourceId, ServiceType = result.ServiceType,
        Status = result.Status, Message = result.Message, CheckedAt = result.CheckedAt,
        ResponseTime = result.ResponseTime, Metadata = new(result.Metadata)
    };
}
