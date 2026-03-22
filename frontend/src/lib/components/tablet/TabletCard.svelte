<script lang="ts">
  import type { TabletInfo } from '../../types/tablet';
  import GlassPanel from '../shared/GlassPanel.svelte';

  let { tablet }: { tablet: TabletInfo } = $props();
</script>

<GlassPanel class="tablet-card">
  <div class="card-header">
    <div class="tablet-icon">
      <svg width="32" height="32" viewBox="0 0 24 24" fill="none" stroke="var(--accent)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
        <rect x="4" y="2" width="16" height="20" rx="2"/>
        <line x1="12" y1="18" x2="12.01" y2="18"/>
      </svg>
    </div>
    <div class="tablet-name-group">
      <h3 class="tablet-name">{tablet.name}</h3>
      <span class="tablet-status">Connected</span>
    </div>
  </div>

  <div class="specs-grid">
    <div class="spec">
      <span class="spec-label">Active Area</span>
      <span class="spec-value">{tablet.specifications.digitizer.width.toFixed(0)} x {tablet.specifications.digitizer.height.toFixed(0)} mm</span>
    </div>
    <div class="spec">
      <span class="spec-label">Resolution</span>
      <span class="spec-value">{tablet.specifications.digitizer.maxX} x {tablet.specifications.digitizer.maxY}</span>
    </div>
    <div class="spec">
      <span class="spec-label">Pressure Levels</span>
      <span class="spec-value">{tablet.specifications.pen.maxPressure.toLocaleString()}</span>
    </div>
    <div class="spec">
      <span class="spec-label">Pen Buttons</span>
      <span class="spec-value">{tablet.specifications.pen.buttonCount}</span>
    </div>
  </div>
</GlassPanel>

<style>
  .card-header {
    display: flex;
    align-items: center;
    gap: var(--space-4);
    margin-bottom: var(--space-5);
  }

  .tablet-icon {
    width: 48px;
    height: 48px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--accent-muted);
    border-radius: var(--radius-md);
  }

  .tablet-name-group {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
  }

  .tablet-name {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0;
  }

  .tablet-status {
    font-size: var(--font-size-xs);
    color: var(--success);
    font-weight: var(--font-weight-medium);
  }

  .specs-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-4);
  }

  .spec {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
  }

  .spec-label {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }

  .spec-value {
    font-size: var(--font-size-base);
    font-weight: var(--font-weight-medium);
    color: var(--text-primary);
  }
</style>
