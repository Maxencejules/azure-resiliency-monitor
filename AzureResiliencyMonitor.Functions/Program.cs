using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AzureResiliencyMonitor.Core.Interfaces;
using AzureResiliencyMonitor.Core.Services;
using AzureResiliencyMonitor.Core.Monitors;

var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .ConfigureServices(services =>
    {
        services.AddLogging();
        
        // Register monitors
        services.AddSingleton<AppServiceMonitor>();
        
        // Register health check service
        services.AddSingleton<IHealthCheckService, HealthCheckService>(provider =>
        {
            var service = new HealthCheckService(
                provider.GetRequiredService<ILogger<HealthCheckService>>());
            
            // Register all monitors
            service.RegisterMonitor(provider.GetRequiredService<AppServiceMonitor>());
            
            return service;
        });
    })
    .Build();

host.Run();