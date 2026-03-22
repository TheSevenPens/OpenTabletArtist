import type { TabletInfo } from '../types/tablet';
import type { Settings } from '../types/settings';

const BASE = '/api';

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    throw new Error(`API error: ${res.status} ${res.statusText}`);
  }
  return res.json();
}

export async function fetchTablets(): Promise<TabletInfo[]> {
  return request<TabletInfo[]>('/tablets');
}

export async function fetchSettings(): Promise<Settings> {
  return request<Settings>('/settings');
}

export async function saveSettings(settings: Settings): Promise<void> {
  await request('/settings', {
    method: 'POST',
    body: JSON.stringify(settings),
  });
}

export async function fetchAppInfo(): Promise<Record<string, string>> {
  return request<Record<string, string>>('/app-info');
}
