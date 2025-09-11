export interface HealthCheckResult {
    serviceName: string;
    resourceId: string;
    serviceType: string;
    status: 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unknown';
    message: string;
    checkedAt: string;
    responseTime: string;
    metadata: Record<string, any>;
}