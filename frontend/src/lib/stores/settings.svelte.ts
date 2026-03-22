import type { Settings, Profile } from '../types/settings';

export const settingsStore = createSettingsStore();

function createSettingsStore() {
  let settings = $state<Settings | null>(null);

  return {
    get current() { return settings; },
    get activeProfile(): Profile | null {
      return settings?.profiles?.[0] ?? null;
    },
    set(s: Settings) { settings = s; },
    clear() { settings = null; }
  };
}
