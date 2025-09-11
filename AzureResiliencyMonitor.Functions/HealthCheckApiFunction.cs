using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using AzureResiliencyMonitor.Core.Interfaces;
using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AzureResiliencyMonitor.Functions;

public class HealthCheckApiFunction
{
    private readonly IHealthCheckService _healthCheckService;
    private readonly ILogger<HealthCheckApiFunction> _logger;

    public HealthCheckApiFunction(
        IHealthCheckService healthCheckService,
        ILogger<HealthCheckApiFunction> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    [Function("GetCurrentHealth")]
    public async Task<HttpResponseData> GetCurrentHealth(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/current")] 
        HttpRequestData req)
    {
        _logger.LogInformation("Getting current health status");
        
        var results = await _healthCheckService.CheckAllServicesAsync();
        
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        
        var options = new JsonSerializerOptions 
        { 
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }  // This converts enums to strings
        };
        
        var json = JsonSerializer.Serialize(results, options);
        
        await response.WriteStringAsync(json);
        return response;
    }

    [Function("HealthCheck")]
    public HttpResponseData HealthCheck(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health")] 
        HttpRequestData req)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/plain");
        response.WriteString("API is healthy!");
        return response;
    }
}