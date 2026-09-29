using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Core.Pipeline;
using Azure.ResourceManager;
using AzureResiliencyMonitor.Core.Models;
using AzureResiliencyMonitor.Core.Monitors;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AzureResiliencyMonitor.Tests;

public sealed class AppServiceMonitorTests
{
    private const string Subscription = "11111111-1111-1111-1111-111111111111";
    private const string Resource = $"/subscriptions/{Subscription}/resourceGroups/demo/providers/Microsoft.Web/sites/demo-app";

    [Fact]
    public void DefaultDependencyInjectionStillResolvesWithoutRegisteredArmClient()
    {
        var services = new ServiceCollection();
        services.AddSingleton<ILogger<AppServiceMonitor>>(NullLogger<AppServiceMonitor>.Instance);
        services.AddSingleton<AppServiceMonitor>();
        using var provider = services.BuildServiceProvider();

        Assert.Equal(ServiceType.AppService, provider.GetRequiredService<AppServiceMonitor>().ServiceType);
    }

    [Fact]
    public async Task RunningAppUsesAuthenticatedArmReadAndReportsHealthy()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Site("Running")));
        using var client = new HttpClient(handler);
        var monitor = Create(client);

        var result = await monitor.CheckHealthAsync(Resource);

        Assert.Equal(HealthStatus.Healthy, result.Status);
        Assert.Equal("Running", result.Metadata["state"]);
        Assert.True(result.ResponseTime >= TimeSpan.Zero);
        AssertRead(Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task UnhealthyAppSendsRestartRequestAndDoesNotClaimRecoveredHealth()
    {
        using var handler = new FakeHandler((request, _) => Task.FromResult(
            request.Method == HttpMethod.Get ? Site("Stopped") : new HttpResponseMessage(HttpStatusCode.OK)));
        using var client = new HttpClient(handler);
        var monitor = Create(client);

        var observation = await monitor.CheckHealthAsync(Resource);
        var accepted = await monitor.AttemptRecoveryAsync(Resource);

        Assert.Equal(HealthStatus.Unhealthy, observation.Status);
        Assert.True(accepted);
        Assert.Collection(handler.Requests, AssertRead, AssertRead, AssertRestart);
    }

    [Fact]
    public async Task RunningAppDoesNotSendRestartRequest()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Site("Running")));
        using var client = new HttpClient(handler);

        Assert.False(await Create(client).AttemptRecoveryAsync(Resource));
        AssertRead(Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task FailedArmReadIsUnknownAndPreservesUsefulError()
    {
        using var handler = new FakeHandler((_, _) => Task.FromResult(Error(HttpStatusCode.Forbidden)));
        using var client = new HttpClient(handler);

        var result = await Create(client).CheckHealthAsync(Resource);

        Assert.Equal(HealthStatus.Unknown, result.Status);
        Assert.Contains("Access denied", result.Message);
        AssertRead(Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedRecoveryReadOrRestartReturnsFalse(bool failRestart)
    {
        using var handler = new FakeHandler((request, _) => Task.FromResult(
            failRestart && request.Method == HttpMethod.Get ? Site("Stopped") : Error(HttpStatusCode.Forbidden)));
        using var client = new HttpClient(handler);

        Assert.False(await Create(client).AttemptRecoveryAsync(Resource));
        if (failRestart) Assert.Collection(handler.Requests, AssertRead, AssertRestart);
        else AssertRead(Assert.Single(handler.Requests));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationDuringHealthOrRecoveryReadPropagates(bool recovery)
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(async (_, token) =>
        {
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancelled transport should not complete.");
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();
        var monitor = Create(client);

        Task operation = recovery
            ? monitor.AttemptRecoveryAsync(Resource, cancellation.Token)
            : monitor.CheckHealthAsync(Resource, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        AssertRead(Assert.Single(handler.Requests));
    }

    [Fact]
    public async Task CancellationDuringRestartPropagatesInsteadOfReturningFailure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new FakeHandler(async (request, token) =>
        {
            if (request.Method == HttpMethod.Get) return Site("Stopped");
            entered.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Cancelled transport should not complete.");
        });
        using var client = new HttpClient(handler);
        using var cancellation = new CancellationTokenSource();

        var operation = Create(client).AttemptRecoveryAsync(Resource, cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => operation.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Collection(handler.Requests, AssertRead, AssertRestart);
    }

    private static AppServiceMonitor Create(HttpClient client)
    {
        var options = new ArmClientOptions { Transport = new HttpClientTransport(client) };
        options.Retry.MaxRetries = 0;
        var credential = DelegatedTokenCredential.Create((_, _) =>
            new AccessToken("test-token", DateTimeOffset.UtcNow.AddHours(1)));
        return new AppServiceMonitor(NullLogger<AppServiceMonitor>.Instance,
            new ArmClient(credential, Subscription, options));
    }

    private static HttpResponseMessage Site(string state) => Json(HttpStatusCode.OK, JsonSerializer.Serialize(new
    {
        id = Resource,
        name = "demo-app",
        type = "Microsoft.Web/sites",
        location = "westeurope",
        properties = new { state }
    }));

    private static HttpResponseMessage Error(HttpStatusCode status) => Json(status,
        """{"error":{"code":"AuthorizationFailed","message":"Access denied"}}""");

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private static void AssertRead(RecordedRequest request)
    {
        Assert.Equal(HttpMethod.Get, request.Method);
        AssertArmRequest(request, Resource);
    }

    private static void AssertRestart(RecordedRequest request)
    {
        Assert.Equal(HttpMethod.Post, request.Method);
        AssertArmRequest(request, Resource + "/restart");
        Assert.Contains("softRestart=true", request.Uri.Query);
        Assert.Contains("synchronous=false", request.Uri.Query);
    }

    private static void AssertArmRequest(RecordedRequest request, string path)
    {
        Assert.Equal("https", request.Uri.Scheme);
        Assert.Equal("management.azure.com", request.Uri.Host);
        Assert.Equal(path, request.Uri.AbsolutePath);
        Assert.Contains("api-version=", request.Uri.Query);
        Assert.Equal("Bearer test-token", request.Authorization);
    }

    private sealed record RecordedRequest(HttpMethod Method, Uri Uri, string? Authorization);

    private sealed class FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(request.Method, request.RequestUri!, request.Headers.Authorization?.ToString()));
            return send(request, cancellationToken);
        }
    }
}
