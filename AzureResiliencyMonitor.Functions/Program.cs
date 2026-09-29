using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Models;
using AzureResiliencyMonitor.Core.Monitors;
using AzureResiliencyMonitor.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureAppConfiguration(configuration => configuration.AddEnvironmentVariables())
    .ConfigureServices((context, services) =>
    {
        services.AddSingleton(TimeProvider.System);
        var development = context.Configuration["AZURE_FUNCTIONS_ENVIRONMENT"] == "Development";
        if (development) services.AddSingleton<IServiceMonitor, MockServiceMonitor>();
        else services.AddSingleton<IServiceMonitor, AppServiceMonitor>();
        services.AddOptions<MonitoringOptions>()
            .Configure(options =>
            {
                options.Resources = (context.Configuration["MonitoredResources"] ?? "")
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (development && options.Resources.Length == 0)
                    options.Resources = ["mock://app/healthy", "mock://app/degraded", "mock://app/recovery-fails"];
                options.RecoveryEnabled = context.Configuration.GetValue<bool>("RecoveryEnabled");
                options.RecoveryCooldown = TimeSpan.FromSeconds(
                    context.Configuration.GetValue<double>("RecoveryCooldownSeconds", 300));
            })
            .Validate(options => options.RecoveryCooldown > TimeSpan.Zero,
                "RecoveryCooldownSeconds must be positive.").ValidateOnStart();
        services.AddSingleton<IHealthCheckService, HealthCheckService>();
    }).Build();
await host.RunAsync();
