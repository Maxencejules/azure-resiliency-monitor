export interface HealthCheckResult {
    serviceName: string;
    resourceId: string;
    serviceType: string;
    status: 'Healthy' | 'Degraded' | 'Unhealthy' | 'Unknown';
    message: string;
    checkedAt: string;
    responseTime: string;
    responseTimeMs: number;
    metadata: Record<string, unknown> & {
        cpuUsage?: number;
        memoryUsage?: number;
        recoveryOutcome?: 'NotNeeded' | 'Disabled' | 'Cooldown' | 'Started' | 'Failed' | 'Unsupported';
        recoveryAttempted?: boolean;
        recoverySucceeded?: boolean;
        nextRecoveryAt?: string;
    };
}
