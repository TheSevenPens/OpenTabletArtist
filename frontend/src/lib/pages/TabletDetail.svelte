<script lang="ts">
  import AreaMapping from './AreaMapping.svelte';
  import Bindings from './Bindings.svelte';
  import Filters from './Filters.svelte';
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import { settingsStore } from '../stores/settings.svelte';

  let { tabletName, subTab = 'area' }: { tabletName: string; subTab?: string } = $props();

  let profile = $derived(
    settingsStore.current?.profiles?.find(p => p.tablet === tabletName) ?? null
  );

  const tabs = [
    { id: 'area', label: 'Area Mapping' },
    { id: 'bindings', label: 'Bindings' },
    { id: 'filters', label: 'Filters' },
  ];
</script>

<div class="tablet-detail">
  <header class="detail-header">
    <a href="#/tablets" class="back-btn glass glass-interactive">
      <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="15 18 9 12 15 6"/>
      </svg>
    </a>
    <div class="detail-title-group">
      <h1 class="detail-title">{tabletName}</h1>
      {#if profile?.outputMode}
        <span class="detail-mode">{profile.outputMode.path?.split('.').pop()}</span>
      {/if}
    </div>
  </header>

  {#if profile}
    <nav class="detail-tabs">
      {#each tabs as tab}
        <a
          href="#/tablets/{encodeURIComponent(tabletName)}/{tab.id}"
          class="detail-tab"
          class:detail-tab-active={subTab === tab.id}
        >
          {tab.label}
        </a>
      {/each}
    </nav>

    <div class="detail-content">
      {#if subTab === 'bindings'}
        <Bindings {profile} />
      {:else if subTab === 'filters'}
        <Filters {profile} />
      {:else}
        <AreaMapping {profile} />
      {/if}
    </div>
  {:else}
    <GlassPanel>
      <div class="placeholder">
        <h3>Profile Not Found</h3>
        <p>No configuration found for "{tabletName}". The tablet may need to be connected first.</p>
      </div>
    </GlassPanel>
  {/if}
</div>

<style>
  .tablet-detail {
    max-width: 1200px;
  }

  .detail-header {
    display: flex;
    align-items: center;
    gap: var(--space-4);
    margin-bottom: var(--space-5);
  }

  .back-btn {
    width: 36px;
    height: 36px;
    display: flex;
    align-items: center;
    justify-content: center;
    border-radius: var(--radius-sm);
    color: var(--text-secondary);
    text-decoration: none;
    flex-shrink: 0;
  }

  .back-btn:hover {
    color: var(--text-primary);
  }

  .detail-title-group {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
    min-width: 0;
  }

  .detail-title {
    font-size: var(--font-size-2xl);
    font-weight: var(--font-weight-bold);
    color: var(--text-primary);
    margin: 0;
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .detail-mode {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    font-family: var(--font-mono);
  }

  .detail-tabs {
    display: flex;
    gap: var(--space-1);
    border-bottom: 1px solid var(--divider);
    margin-bottom: var(--space-6);
  }

  .detail-tab {
    padding: var(--space-3) var(--space-5);
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    color: var(--text-muted);
    text-decoration: none;
    border-bottom: 2px solid transparent;
    margin-bottom: -1px;
    transition: all var(--transition-fast);
  }

  .detail-tab:hover {
    color: var(--text-secondary);
  }

  .detail-tab-active {
    color: var(--accent);
    border-bottom-color: var(--accent);
  }

  .detail-content {
    min-height: 300px;
  }

  .placeholder { display: flex; flex-direction: column; align-items: center; text-align: center; gap: var(--space-4); padding: var(--space-10) 0; }
  .placeholder h3 { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-secondary); margin: 0; }
  .placeholder p { font-size: var(--font-size-sm); color: var(--text-muted); max-width: 320px; margin: 0; }
</style>
