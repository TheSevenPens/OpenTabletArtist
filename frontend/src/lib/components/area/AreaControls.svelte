<script lang="ts">
  import type { AreaSettings } from '../../types/settings';
  import GlassPanel from '../shared/GlassPanel.svelte';

  let {
    label = 'Area',
    area,
    locked = false,
    color = 'var(--accent)',
  }: {
    label?: string;
    area: AreaSettings;
    locked?: boolean;
    color?: string;
  } = $props();
</script>

<GlassPanel padding="var(--space-5)">
  <div class="controls-header">
    <div class="label-dot" style="background: {color}"></div>
    <h4 class="controls-label">{label}</h4>
    {#if locked}
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <rect x="3" y="11" width="18" height="11" rx="2" ry="2"/><path d="M7 11V7a5 5 0 0 1 10 0v4"/>
      </svg>
    {/if}
  </div>

  <div class="fields">
    <div class="field-row">
      <label class="field">
        <span class="field-label">W</span>
        <input type="number" class="field-input glass-subtle" value={area.width.toFixed(1)} step="0.1" />
      </label>
      <label class="field">
        <span class="field-label">H</span>
        <input type="number" class="field-input glass-subtle" value={area.height.toFixed(1)} step="0.1" />
      </label>
    </div>
    <div class="field-row">
      <label class="field">
        <span class="field-label">X</span>
        <input type="number" class="field-input glass-subtle" value={area.x.toFixed(1)} step="0.1" />
      </label>
      <label class="field">
        <span class="field-label">Y</span>
        <input type="number" class="field-input glass-subtle" value={area.y.toFixed(1)} step="0.1" />
      </label>
    </div>
    <div class="field-row">
      <label class="field field-full">
        <span class="field-label">Rotation</span>
        <input type="number" class="field-input glass-subtle" value={area.rotation.toFixed(1)} step="0.5" min="-180" max="180" />
        <span class="field-unit">deg</span>
      </label>
    </div>
  </div>
</GlassPanel>

<style>
  .controls-header {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    margin-bottom: var(--space-4);
  }

  .label-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .controls-label {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0;
    flex: 1;
  }

  .fields {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }

  .field-row {
    display: flex;
    gap: var(--space-3);
  }

  .field {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    flex: 1;
  }

  .field-full {
    flex: 1;
  }

  .field-label {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    font-weight: var(--font-weight-medium);
    width: 14px;
    text-align: right;
    flex-shrink: 0;
  }

  .field-input {
    width: 100%;
    padding: var(--space-1) var(--space-2);
    border-radius: var(--radius-xs);
    font-size: var(--font-size-sm);
    font-family: var(--font-mono);
    background: var(--glass-bg);
    border: 1px solid var(--glass-border);
    color: var(--text-primary);
    outline: none;
    transition: border-color var(--transition-fast);
  }

  .field-input:focus {
    border-color: var(--accent);
  }

  .field-unit {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    flex-shrink: 0;
  }
</style>
