using System.Net;
using System.Security.Claims;
using System.Text.Json;
using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using AzureResiliencyMonitor.Core.Monitors;
using AzureResiliencyMonitor.Core.Services;
using AzureResiliencyMonitor.Functions;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace AzureResiliencyMonitor.Tests;

public sealed class HealthCheckTests
{
    private const string Resource = "mock://app/unhealthy";

    [Fact]
    public void EmptyCacheDoesNotCheckResources()
    {
        var monitor = new FakeMonitor();
        Assert.Empty(Create(monitor).GetCurrentHealth());
        Assert.Equal(0, monitor.CheckCalls);
        Assert.Equal(0, monitor.RecoveryCalls);
    }

    [Fact]
    public async Task HttpPollingReadsOnlyCachedSnapshotWithNumericMilliseconds()
    {
        var monitor = new FakeMonitor();
        var service = Create(monitor);
        await service.CheckAllServicesAsync();
        var function = new HealthCheckApiFunction(service, NullLogger<HealthCheckApiFunction>.Instance);
        for (var poll = 0; poll < 20; poll++)
        {
            var response = await function.GetCurrentHealth(new FakeRequest());
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            response.Body.Position = 0;
            using var json = await JsonDocument.ParseAsync(response.Body);
            var item = json.RootElement[0];
            Assert.Equal(120, item.GetProperty("responseTimeMs").GetDouble());
            Assert.Equal("Unhealthy", item.GetProperty("status").GetString());
            Assert.Equal("Started", item.GetProperty("metadata").GetProperty("recoveryOutcome").GetString());
        }
        Assert.Equal(1, monitor.CheckCalls);
        Assert.Equal(1, monitor.RecoveryCalls);
    }

    [Fact]
    public async Task UnhealthyChecksRespectCooldownIncludingExactBoundary()
    {
        var clock = new TestClock();
        var monitor = new FakeMonitor();
        var service = Create(monitor, clock);
        Assert.Equal("Started", (await service.CheckServiceAsync(Resource)).Metadata["recoveryOutcome"]);
        clock.Advance(TimeSpan.FromMinutes(4));
        Assert.Equal("Cooldown", (await service.CheckServiceAsync(Resource)).Metadata["recoveryOutcome"]);
        Assert.Equal(1, monitor.RecoveryCalls);
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal("Started", (await service.CheckServiceAsync(Resource)).Metadata["recoveryOutcome"]);
        Assert.Equal(2, monitor.RecoveryCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRecoveryIsVisibleAndConsumesCooldown(bool throws)
    {
        var monitor = new FakeMonitor { RecoveryAccepted = false, ThrowRecovery = throws };
        var service = Create(monitor);
        var result = await service.CheckServiceAsync(Resource);
        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Equal("Failed", result.Metadata["recoveryOutcome"]);
        Assert.Equal(true, result.Metadata["recoveryAttempted"]);
        Assert.Equal(false, result.Metadata["recoverySucceeded"]);
        if (throws) Assert.Equal("Recovery transport failed", result.Metadata["recoveryError"]);
        Assert.Equal("Cooldown", (await service.CheckServiceAsync(Resource)).Metadata["recoveryOutcome"]);
        Assert.Equal(1, monitor.RecoveryCalls);
    }

    [Fact]
    public async Task RecoveryIsExplicitlyDisabledByDefault()
    {
        var monitor = new FakeMonitor();
        var service = Create(monitor, enabled: false);
        Assert.Equal("Disabled", (await service.CheckServiceAsync(Resource)).Metadata["recoveryOutcome"]);
        Assert.Equal(0, monitor.RecoveryCalls);
    }

    [Theory]
    [InlineData(HealthStatus.Healthy)]
    [InlineData(HealthStatus.Degraded)]
    [InlineData(HealthStatus.Unknown)]
    public async Task OnlyUnhealthyResourcesCanRecover(HealthStatus status)
    {
        var monitor = new FakeMonitor { Status = status };
        var result = await Create(monitor).CheckServiceAsync(Resource);
        Assert.Equal("NotNeeded", result.Metadata["recoveryOutcome"]);
        Assert.Equal(0, monitor.RecoveryCalls);
    }

    [Fact]
    public async Task UnsupportedRealServicesDoNotFallBackToAppService()
    {
        var monitor = new FakeMonitor();
        var result = await Create(monitor).CheckServiceAsync(
            "/subscriptions/test/resourceGroups/test/providers/Microsoft.DocumentDB/databaseAccounts/db");
        Assert.Equal(HealthStatus.Unknown, result.Status);
        Assert.Equal(ServiceType.CosmosDB, result.ServiceType);
        Assert.Equal("Unsupported", result.Metadata["recoveryOutcome"]);
        Assert.Equal(0, monitor.CheckCalls);
    }

    [Fact]
    public async Task CheckFailureProducesUnknownSnapshotAndDoesNotRecover()
    {
        var monitor = new FakeMonitor { ThrowCheck = true };
        var service = Create(monitor);
        await service.CheckAllServicesAsync();
        var result = Assert.Single(service.GetCurrentHealth());
        Assert.Equal(HealthStatus.Unknown, result.Status);
        Assert.Equal("Health transport failed", result.Metadata["checkError"]);
        Assert.Equal(0, monitor.RecoveryCalls);
    }

    [Fact]
    public async Task ReturnedSnapshotsCannotMutateCachedState()
    {
        var service = Create(new FakeMonitor());
        await service.CheckAllServicesAsync();
        var snapshot = Assert.Single(service.GetCurrentHealth());
        snapshot.Status = HealthStatus.Healthy;
        snapshot.Metadata["recoveryOutcome"] = "tampered";
        var unchanged = Assert.Single(service.GetCurrentHealth());
        Assert.Equal(HealthStatus.Unhealthy, unchanged.Status);
        Assert.Equal("Started", unchanged.Metadata["recoveryOutcome"]);
    }

    [Fact]
    public async Task ConcurrentChecksDoNotIssueCompetingRecoveryRequests()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var monitor = new FakeMonitor
        {
            BeforeRecovery = async () => { entered.SetResult(); await release.Task; }
        };
        var service = Create(monitor);
        var first = service.CheckServiceAsync(Resource);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var second = service.CheckServiceAsync(Resource);
        release.SetResult();
        await Task.WhenAll(first, second);
        Assert.Equal(2, monitor.CheckCalls);
        Assert.Equal(1, monitor.RecoveryCalls);
        Assert.Equal("Cooldown", (await second).Metadata["recoveryOutcome"]);
    }

    [Fact]
    public async Task CancelledCheckPreservesSnapshotAndReleasesCheckGate()
    {
        var monitor = new FakeMonitor { Status = HealthStatus.Healthy };
        var service = Create(monitor);
        await service.CheckAllServicesAsync();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.CheckServiceAsync(Resource, cancellation.Token));
        Assert.Equal(HealthStatus.Healthy, Assert.Single(service.GetCurrentHealth()).Status);
        await service.CheckAllServicesAsync();
        Assert.Equal(2, monitor.CheckCalls);
    }

    [Fact]
    public async Task TimerPropagatesInvocationCancellation()
    {
        var monitor = new FakeMonitor();
        var function = new HealthCheckFunction(NullLogger<HealthCheckFunction>.Instance, Create(monitor));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => function.RunAsync(null!, cancellation.Token));
        Assert.Equal(0, monitor.CheckCalls);
    }

    [Theory]
    [InlineData("healthy", HealthStatus.Healthy, true)]
    [InlineData("degraded", HealthStatus.Degraded, true)]
    [InlineData("recovery-fails", HealthStatus.Unhealthy, false)]
    public async Task MockScenariosAreDeterministic(string scenario, HealthStatus status, bool recovery)
    {
        var monitor = new MockServiceMonitor(NullLogger<MockServiceMonitor>.Instance, new TestClock());
        var resource = $"mock://app/{scenario}";
        var first = await monitor.CheckHealthAsync(resource);
        var second = await monitor.CheckHealthAsync(resource);
        Assert.Equal(status, first.Status);
        Assert.Equal(first.Status, second.Status);
        Assert.Equal(first.CheckedAt, second.CheckedAt);
        Assert.Equal(120, first.ResponseTimeMs);
        Assert.Equal(recovery, await monitor.AttemptRecoveryAsync(resource));
    }

    private static HealthCheckService Create(FakeMonitor monitor, TestClock? clock = null, bool enabled = true) =>
        new(NullLogger<HealthCheckService>.Instance, [monitor],
            Options.Create(new MonitoringOptions { Resources = [Resource], RecoveryEnabled = enabled }),
            clock ?? new TestClock());

    private sealed class TestClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class FakeMonitor : IServiceMonitor
    {
        public ServiceType ServiceType => ServiceType.AppService;
        public HealthStatus Status { get; set; } = HealthStatus.Unhealthy;
        public bool RecoveryAccepted { get; set; } = true;
        public bool ThrowRecovery { get; set; }
        public bool ThrowCheck { get; set; }
        public Func<Task>? BeforeRecovery { get; set; }
        public int CheckCalls { get; private set; }
        public int RecoveryCalls { get; private set; }
        public Task<HealthCheckResult> CheckHealthAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            CheckCalls++;
            if (ThrowCheck) throw new InvalidOperationException("Health transport failed");
            return Task.FromResult(new HealthCheckResult
            {
                ResourceId = resourceId, Status = Status, ServiceType = ServiceType,
                ResponseTime = TimeSpan.FromMilliseconds(120)
            });
        }
        public async Task<bool> AttemptRecoveryAsync(string resourceId, CancellationToken cancellationToken = default)
        {
            RecoveryCalls++;
            if (BeforeRecovery is not null) await BeforeRecovery();
            if (ThrowRecovery) throw new InvalidOperationException("Recovery transport failed");
            return RecoveryAccepted;
        }
    }

    private sealed class FakeFunctionContext : FunctionContext
    {
        public override string InvocationId => "test-invocation";
        public override string FunctionId => "test-function";
        public override TraceContext TraceContext => throw new NotSupportedException();
        public override BindingContext BindingContext => throw new NotSupportedException();
        public override RetryContext RetryContext => throw new NotSupportedException();
        public override IServiceProvider InstanceServices { get; set; } = null!;
        public override FunctionDefinition FunctionDefinition => throw new NotSupportedException();
        public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
        public override IInvocationFeatures Features => throw new NotSupportedException();
    }

    private sealed class FakeRequest() : HttpRequestData(new FakeFunctionContext())
    {
        public override Stream Body => Stream.Null;
        public override HttpHeadersCollection Headers { get; } = new();
        public override IReadOnlyCollection<IHttpCookie> Cookies => [];
        public override Uri Url => new("http://localhost/api/health/current");
        public override IEnumerable<ClaimsIdentity> Identities => [];
        public override string Method => "GET";
        public override HttpResponseData CreateResponse() => new FakeResponse(FunctionContext);
    }

    private sealed class FakeResponse(FunctionContext context) : HttpResponseData(context)
    {
        public override HttpStatusCode StatusCode { get; set; }
        public override HttpHeadersCollection Headers { get; set; } = new();
        public override Stream Body { get; set; } = new MemoryStream();
        public override HttpCookies Cookies => null!;
    }
}
