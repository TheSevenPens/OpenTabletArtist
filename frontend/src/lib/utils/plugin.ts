import type { PluginRef } from '../types/settings';

/**
 * Extract a human-readable display name from a plugin reference.
 * Checks for a 'button' setting value first, then falls back to the
 * short class name from the plugin path.
 */
export function getPluginDisplayName(plugin: PluginRef | null | undefined, fallback = 'None'): string {
  if (!plugin) return fallback;
  const buttonSetting = plugin.settings?.find(s => s.property === 'button');
  if (buttonSetting?.value) return String(buttonSetting.value);
  return plugin.path?.split('.').pop() ?? fallback;
}

/**
 * Extract the short class name from a plugin path.
 * e.g. "VoiDPlugins.OutputMode.WinInkAbsoluteMode" → "WinInkAbsoluteMode"
 */
export function getPluginShortName(plugin: PluginRef | null | undefined, fallback = 'None'): string {
  return plugin?.path?.split('.').pop() ?? fallback;
}

/**
 * Simple pluralization helper.
 */
export function pluralize(count: number, singular: string, plural?: string): string {
  return count === 1 ? singular : (plural ?? singular + 's');
}
