import type { TabletInfo } from '../types/tablet';
import type { Settings } from '../types/settings';

const BASE = '/api';

// OTD daemon sends PascalCase JSON. Convert to camelCase for our TypeScript types.
function toCamelCase(obj: unknown): unknown {
  if (Array.isArray(obj)) return obj.map(toCamelCase);
  if (obj !== null && typeof obj === 'object') {
    return Object.fromEntries(
      Object.entries(obj as Record<string, unknown>).map(([key, val]) => [
        key.charAt(0).toLowerCase() + key.slice(1),
        toCamelCase(val),
      ])
    );
  }
  return obj;
}

async function request<T>(path: string, options?: RequestInit): Promise<T> {
  const res = await fetch(`${BASE}${path}`, {
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    throw new Error(`API error: ${res.status} ${res.statusText}`);
  }
  const json = await res.json();
  return toCamelCase(json) as T;
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
