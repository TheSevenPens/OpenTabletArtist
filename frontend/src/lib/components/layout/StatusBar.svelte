<script lang="ts">
  import ConnectionDot from '../shared/ConnectionDot.svelte';
  import ThemeToggle from '../shared/ThemeToggle.svelte';
  import { tabletsStore } from '../../stores/tablets.svelte';
</script>

<footer class="statusbar glass">
  <div class="status-left">
    <ConnectionDot />
  </div>
  <div class="status-center">
    {#if tabletsStore.hasTablet}
      <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <rect x="4" y="2" width="16" height="20" rx="2"/>
        <line x1="12" y1="18" x2="12.01" y2="18"/>
      </svg>
      <span class="tablet-name">{tabletsStore.current?.name}</span>
    {:else}
      <span class="no-tablet">No tablet connected</span>
    {/if}
  </div>
  <div class="status-right">
    <ThemeToggle />
  </div>
</footer>

<style>
  .statusbar {
    grid-area: status;
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: 0 var(--space-4);
    height: var(--statusbar-height);
    border-radius: 0;
    border-left: none;
    border-right: none;
    border-bottom: none;
    border-top: var(--glass-border-width) solid var(--glass-border);
    z-index: 10;
  }

  .status-left, .status-right {
    display: flex;
    align-items: center;
  }

  .status-center {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    color: var(--text-secondary);
    font-size: var(--font-size-xs);
  }

  .tablet-name {
    font-weight: var(--font-weight-medium);
    color: var(--text-primary);
  }

  .no-tablet {
    color: var(--text-muted);
    font-style: italic;
  }
</style>
