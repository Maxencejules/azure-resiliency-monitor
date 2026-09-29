import type { HealthCheckResult } from '../types';

export const demoScenarios = ['healthy', 'degraded', 'outage', 'recovered'] as const;
export type DemoScenario = (typeof demoScenarios)[number];

export function getDemoScenario(value: string | null): DemoScenario | undefined {
  return demoScenarios.find((scenario) => scenario === value);
}

export function getDemoHealth(scenario: DemoScenario, now: Date): HealthCheckResult[] {
  const services: HealthCheckResult[] = [
    {
      serviceName: 'Web API', resourceId: 'demo/web-api', serviceType: 'Mock', status: 'Healthy',
      message: 'The service is responding normally.',
      checkedAt: new Date(now.getTime() - 4000).toISOString(),
      responseTime: '00:00:00.1250000', responseTimeMs: 125,
      metadata: { cpuUsage: 32, memoryUsage: 48, recoveryOutcome: 'NotNeeded' },
    },
    {
      serviceName: 'Order worker', resourceId: 'demo/order-worker', serviceType: 'Mock', status: 'Healthy',
      message: 'The worker is processing requests.',
      checkedAt: new Date(now.getTime() - 4000).toISOString(),
      responseTime: '00:00:00.2400000', responseTimeMs: 240,
      metadata: { cpuUsage: 41, memoryUsage: 56, recoveryOutcome: 'NotNeeded' },
    },
  ];

  if (scenario === 'degraded') {
    Object.assign(services[0], {
      status: 'Degraded', message: 'The service is responding slowly; no restart was requested.',
      responseTime: '00:00:01.2500000', responseTimeMs: 1250,
    });
  } else if (scenario === 'outage') {
    Object.assign(services[1], {
      status: 'Unhealthy', message: 'The worker is unavailable. A restart was requested.',
      responseTime: '00:00:05', responseTimeMs: 5000,
      metadata: { recoveryOutcome: 'Started', recoveryAttempted: true, recoverySucceeded: true },
    });
  } else if (scenario === 'recovered') {
    Object.assign(services[1], {
      message: 'A subsequent check confirmed that the worker recovered.',
      metadata: { recoveryOutcome: 'NotNeeded', recoveryAttempted: false },
    });
  }
  return services;
}
