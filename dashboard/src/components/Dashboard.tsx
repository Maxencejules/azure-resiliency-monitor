import { useEffect, useRef, useState } from 'react';
import axios from 'axios';
import { getDemoHealth, type DemoScenario } from '../data/demo';
import type { HealthCheckResult } from '../types';
import './Dashboard.css';

const API_URL = (import.meta.env.VITE_API_BASE_URL || '/api').replace(/\/$/, '');
export const REFRESH_INTERVAL_MS = 10_000;
export const STALE_AFTER_MS = 120_000;
const statuses = ['Healthy', 'Degraded', 'Unhealthy', 'Unknown'] as const;
const statusColors = { Healthy: '#047857', Degraded: '#a16207', Unhealthy: '#b91c1c', Unknown: '#4b5563' };
const recoveryLabels = {
  NotNeeded: 'No recovery needed', Disabled: 'Recovery disabled',
  Cooldown: 'Waiting for recovery cooldown', Started: 'Restart requested',
  Failed: 'Restart failed', Unsupported: 'Recovery unsupported',
};

function isHealthResult(value: unknown): value is HealthCheckResult {
  if (typeof value !== 'object' || value === null) return false;
  const result = value as Partial<HealthCheckResult>;
  return typeof result.serviceName === 'string'
    && typeof result.resourceId === 'string'
    && typeof result.serviceType === 'string'
    && typeof result.message === 'string'
    && typeof result.metadata === 'object' && result.metadata !== null
    && !Array.isArray(result.metadata)
    && statuses.includes(result.status as HealthCheckResult['status'])
    && typeof result.checkedAt === 'string'
    && Number.isFinite(Date.parse(result.checkedAt))
    && typeof result.responseTimeMs === 'number'
    && Number.isFinite(result.responseTimeMs) && result.responseTimeMs >= 0;
}

export function Dashboard({ demoScenario }: { demoScenario?: DemoScenario }) {
  const [services, setServices] = useState<HealthCheckResult[]>([]);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null);
  const [now, setNow] = useState(Date.now());
  const refresh = useRef<() => void>(() => {});

  useEffect(() => {
    let active = true;
    let inFlight = false;
    let controller: AbortController | undefined;

    async function fetchHealth() {
      if (inFlight) return;
      inFlight = true;
      controller = new AbortController();
      setRefreshing(true);
      try {
        const results: unknown = demoScenario ? getDemoHealth(demoScenario, new Date())
          : (await axios.get(`${API_URL}/health/current`, {
              signal: controller.signal, timeout: REFRESH_INTERVAL_MS,
            })).data;
        if (!Array.isArray(results) || !results.every(isHealthResult)) {
          throw new Error('Invalid health response');
        }
        if (active) {
          const receivedAt = new Date();
          setServices(results);
          setLastUpdate(receivedAt);
          setNow(receivedAt.getTime());
          setError(null);
        }
      } catch {
        if (active) setError('Unable to refresh health data. Please try again.');
      } finally {
        inFlight = false;
        if (active) { setLoading(false); setRefreshing(false); }
      }
    }

    refresh.current = () => { void fetchHealth(); };
    void fetchHealth();
    const timer = window.setInterval(() => {
      setNow(Date.now());
      void fetchHealth();
    }, REFRESH_INTERVAL_MS);
    return () => {
      active = false;
      window.clearInterval(timer);
      controller?.abort();
      refresh.current = () => {};
    };
  }, [demoScenario]);

  const isStale = (service: HealthCheckResult) => now - Date.parse(service.checkedAt) > STALE_AFTER_MS;
  const staleCount = services.filter(isStale).length;

  return (
    <main className="dashboard">
      <header className="dashboard-header">
        <div>
          <h1>Azure Resiliency Monitor</h1>
          <p className="last-update">
            {lastUpdate ? `Last successful refresh: ${lastUpdate.toLocaleTimeString()}` : 'No health data received yet'}
          </p>
        </div>
        <button type="button" onClick={() => refresh.current()} disabled={refreshing}>
          {refreshing ? 'Refreshing…' : 'Refresh now'}
        </button>
      </header>
      {demoScenario && <p className="notice">Demo: {demoScenario}. These are simulated health checks.</p>}
      {error && <p className="notice notice-error" role="alert">{error}</p>}
      {staleCount > 0 && (
        <p className="notice notice-warning" role="status">
          Cached health data is stale for {staleCount} {staleCount === 1 ? 'service' : 'services'}.
          {' '}A successful refresh does not mean a new health check has run.
        </p>
      )}
      {loading ? <p className="loading" role="status">Loading services…</p> : (
        <>
          <section className="summary-cards" aria-label="Service summary">
            {statuses.map((status) => (
              <div className="summary-card" key={status} aria-label={`${status} services`}>
                <div className="summary-value" style={{ color: statusColors[status] }}>
                  {services.filter((service) => service.status === status).length}
                </div>
                <div className="summary-label">{status}</div>
              </div>
            ))}
          </section>
          {services.length === 0 && !error && (
            <p className="notice" role="status">No cached health checks yet. Waiting for the first health check.</p>
          )}
          <section className="services-grid" aria-label="Monitored services">
            {services.map((service) => (
              <article key={service.resourceId} className="service-card" aria-label={service.serviceName}>
                <div className="service-header">
                  <h2>{service.serviceName}</h2>
                  <span className="status-badge" style={{ backgroundColor: statusColors[service.status] }}>
                    {service.status}
                  </span>
                </div>
                <div className="service-details">
                  <div className="metric">
                    <span className="metric-label">Response time</span>
                    <span className="metric-value">{Math.round(service.responseTimeMs)} ms</span>
                  </div>
                  {(['cpuUsage', 'memoryUsage'] as const).map((metric) => (
                    typeof service.metadata?.[metric] === 'number' && (
                      <div className="metric" key={metric}>
                        <span className="metric-label">{metric === 'cpuUsage' ? 'CPU usage' : 'Memory usage'}</span>
                        <span className="metric-value">{service.metadata[metric]}%</span>
                      </div>
                    )
                  ))}
                  <div className="metric">
                    <span className="metric-label">Last checked</span>
                    <time className="metric-value" dateTime={service.checkedAt}>
                      {new Date(service.checkedAt).toLocaleTimeString()}
                    </time>
                  </div>
                  {isStale(service) && <span className="stale-badge">Stale snapshot</span>}
                </div>
                <p className="service-message">{service.message}</p>
                {service.metadata?.recoveryOutcome && (
                  <p className="recovery-status">{recoveryLabels[service.metadata.recoveryOutcome]}</p>
                )}
              </article>
            ))}
          </section>
        </>
      )}
    </main>
  );
}
