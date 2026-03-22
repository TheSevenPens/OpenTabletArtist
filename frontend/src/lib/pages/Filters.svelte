<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import type { Profile } from '../types/settings';

  let { profile }: { profile: Profile } = $props();

  let filters = $derived(profile.filters ?? []);
</script>

<div class="filters">
  {#if filters.length}
    <div class="filters-list">
      {#each filters as filter, i}
        <GlassPanel interactive>
          <div class="filter-card">
            <div class="filter-index">{i + 1}</div>
            <div class="filter-info">
              <span class="filter-name">{filter.path?.split('.').pop() ?? 'Unknown'}</span>
              <span class="filter-path">{filter.path}</span>
            </div>
            <div class="filter-status" class:enabled={filter.enable} class:disabled={!filter.enable}>
              {filter.enable ? 'Enabled' : 'Disabled'}
            </div>
          </div>
        </GlassPanel>
      {/each}
    </div>
  {:else}
    <GlassPanel>
      <div class="placeholder">
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>
        </svg>
        <h3>No Filters</h3>
        <p>No input filters are configured for this tablet. Filters like smoothing and anti-chatter can be added through the OTD plugin system.</p>
      </div>
    </GlassPanel>
  {/if}
</div>

<style>
  .filters { max-width: 900px; }

  .filters-list {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
  }

  .filter-card {
    display: flex;
    align-items: center;
    gap: var(--space-4);
  }

  .filter-index {
    width: 28px;
    height: 28px;
    display: flex;
    align-items: center;
    justify-content: center;
    border-radius: var(--radius-full);
    background: var(--accent-muted);
    color: var(--accent);
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-semibold);
    flex-shrink: 0;
  }

  .filter-info {
    flex: 1;
    min-width: 0;
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
  }

  .filter-name {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
  }

  .filter-path {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    font-family: var(--font-mono);
  }

  .filter-status {
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-medium);
    padding: var(--space-1) var(--space-3);
    border-radius: var(--radius-full);
    flex-shrink: 0;
  }

  .enabled {
    background: var(--success-muted);
    color: var(--success);
  }

  .disabled {
    background: var(--glass-bg);
    color: var(--text-muted);
  }

  .placeholder { display: flex; flex-direction: column; align-items: center; text-align: center; gap: var(--space-4); padding: var(--space-10) 0; }
  .placeholder h3 { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-secondary); margin: 0; }
  .placeholder p { font-size: var(--font-size-sm); color: var(--text-muted); max-width: 320px; margin: 0; }
</style>
