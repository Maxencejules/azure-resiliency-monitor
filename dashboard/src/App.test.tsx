import { act, fireEvent, render, screen, within } from '@testing-library/react';
import axios from 'axios';
import { beforeEach, expect, test, vi } from 'vitest';
import App from './App';
import { Dashboard, REFRESH_INTERVAL_MS } from './components/Dashboard';
import { demoScenarios, getDemoHealth } from './data/demo';
import type { HealthCheckResult } from './types';

vi.mock('axios', () => ({ default: { get: vi.fn() } }));
const get = vi.mocked(axios.get);
const now = new Date('2026-09-29T12:00:00.000Z');
const health = (overrides: Partial<HealthCheckResult> = {}): HealthCheckResult => ({
  ...getDemoHealth('healthy', now)[0], ...overrides,
});

beforeEach(() => { get.mockReset(); });

test('shows loading until the initial health request completes', () => {
  get.mockImplementation(() => new Promise(() => {}));
  render(<Dashboard />);
  expect(screen.getByRole('status')).toHaveTextContent('Loading services');
  expect(screen.getByRole('button', { name: /refreshing/i })).toBeDisabled();
});

test('uses numeric milliseconds and displays zero-valued metrics', async () => {
  get.mockResolvedValue({ data: [health({
    responseTime: '00:00:01.2500000', responseTimeMs: 1250,
    metadata: { cpuUsage: 0, memoryUsage: 0, recoveryOutcome: 'Cooldown' },
  })] });
  render(<Dashboard />);
  const card = await screen.findByRole('article', { name: 'Web API' });
  expect(within(card).getByText('1250 ms')).toBeInTheDocument();
  expect(within(card).getAllByText('0%')).toHaveLength(2);
  expect(within(card).getByText('Waiting for recovery cooldown')).toBeInTheDocument();
  expect(within(screen.getByLabelText('Healthy services')).getByText('1')).toBeInTheDocument();
  expect(get).toHaveBeenCalledWith('/api/health/current', expect.objectContaining({ signal: expect.any(AbortSignal) }));
});

test('counts Unknown health without classifying it as healthy', async () => {
  get.mockResolvedValue({ data: [health({ status: 'Unknown' })] });
  render(<Dashboard />);
  await screen.findByRole('article', { name: 'Web API' });
  expect(within(screen.getByLabelText('Unknown services')).getByText('1')).toBeInTheDocument();
  expect(within(screen.getByLabelText('Healthy services')).getByText('0')).toBeInTheDocument();
});

test('distinguishes an empty cache from a failed request', async () => {
  get.mockResolvedValue({ data: [] });
  render(<Dashboard />);
  expect(await screen.findByText(/waiting for the first health check/i)).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('shows a recoverable initial error instead of an empty healthy dashboard', async () => {
  get.mockRejectedValueOnce(new Error('offline'));
  get.mockResolvedValueOnce({ data: [health()] });
  render(<Dashboard />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Unable to refresh health data');
  expect(screen.queryByText(/waiting for the first health check/i)).not.toBeInTheDocument();
  fireEvent.click(screen.getByRole('button', { name: 'Refresh now' }));
  expect(await screen.findByRole('article', { name: 'Web API' })).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
});

test('retains the last snapshot and successful refresh time after a polling failure', async () => {
  vi.useFakeTimers();
  vi.setSystemTime(now);
  get.mockResolvedValueOnce({ data: [health()] }).mockRejectedValueOnce(new Error('offline'));
  render(<Dashboard />);
  await act(async () => { await Promise.resolve(); });
  const successfulRefresh = screen.getByText(/last successful refresh/i).textContent;
  await act(async () => { await vi.advanceTimersByTimeAsync(REFRESH_INTERVAL_MS); });
  expect(screen.getByRole('article', { name: 'Web API' })).toBeInTheDocument();
  expect(screen.getByRole('alert')).toHaveTextContent('Unable to refresh health data');
  expect(screen.getByText(/last successful refresh/i).textContent).toBe(successfulRefresh);
});

test('marks unchanged cached checks stale even when subsequent requests succeed', async () => {
  vi.useFakeTimers();
  vi.setSystemTime(now);
  get.mockResolvedValue({ data: [health({ checkedAt: new Date(now.getTime() - 110_000).toISOString() })] });
  render(<Dashboard />);
  await act(async () => { await Promise.resolve(); });
  expect(screen.queryByText('Stale snapshot')).not.toBeInTheDocument();
  await act(async () => { await vi.advanceTimersByTimeAsync(2 * REFRESH_INTERVAL_MS); });
  expect(screen.getByText('Stale snapshot')).toBeInTheDocument();
  expect(screen.getByText(/cached health data is stale for 1 service/i)).toBeInTheDocument();
  expect(screen.queryByRole('alert')).not.toBeInTheDocument();
  expect(get).toHaveBeenCalledTimes(3);
});

test('does not overlap slow polls and aborts the request when unmounted', async () => {
  vi.useFakeTimers();
  get.mockImplementation(() => new Promise(() => {}));
  const { unmount } = render(<Dashboard />);
  await act(async () => { await vi.advanceTimersByTimeAsync(3 * REFRESH_INTERVAL_MS); });
  expect(get).toHaveBeenCalledTimes(1);
  const signal = get.mock.calls[0][1]?.signal;
  expect(signal?.aborted).toBe(false);
  unmount();
  expect(signal?.aborted).toBe(true);
  await act(async () => { await vi.advanceTimersByTimeAsync(3 * REFRESH_INTERVAL_MS); });
  expect(get).toHaveBeenCalledTimes(1);
});

test('rejects malformed health data instead of displaying NaN latency', async () => {
  get.mockResolvedValue({ data: [{ ...health(), responseTimeMs: '1250' }] });
  render(<Dashboard />);
  expect(await screen.findByRole('alert')).toHaveTextContent('Unable to refresh health data');
  expect(screen.queryByRole('article')).not.toBeInTheDocument();
});

test.each(demoScenarios)('demo=%s renders a deterministic scenario without calling the backend', async (scenario) => {
  window.history.replaceState(null, '', `/?demo=${scenario}`);
  render(<App />);
  expect(await screen.findByRole('article', { name: 'Web API' })).toBeInTheDocument();
  expect(screen.getByText(`Demo: ${scenario}. These are simulated health checks.`)).toBeInTheDocument();
  expect(get).not.toHaveBeenCalled();
  const fixed = getDemoHealth(scenario, now);
  expect(fixed).toEqual(getDemoHealth(scenario, now));
  if (scenario === 'outage') {
    const worker = screen.getByRole('article', { name: 'Order worker' });
    expect(within(worker).getByText('Unhealthy')).toBeInTheDocument();
    expect(within(worker).getByText('Restart requested')).toBeInTheDocument();
  } else if (scenario === 'recovered') {
    expect(screen.getByText(/subsequent check confirmed/i)).toBeInTheDocument();
  } else if (scenario === 'degraded') {
    expect(screen.getByText('1250 ms')).toBeInTheDocument();
  }
});
