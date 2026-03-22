import type { TabletInfo } from './tablet';

export type DaemonEvent =
  | { type: 'tabletsChanged'; data: TabletInfo[] }
  | { type: 'deviceReport'; data: { tablet: string; report: unknown } }
  | { type: 'message'; data: { group: string; message: string; level: string } }
  | { type: 'resynchronize'; data: null };
