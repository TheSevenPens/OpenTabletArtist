<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import TabletCard from '../components/tablet/TabletCard.svelte';
  import { tabletsStore } from '../stores/tablets.svelte';
  import { settingsStore } from '../stores/settings.svelte';
</script>

<div class="dashboard">
  <header class="page-header">
    <h1 class="page-title">Dashboard</h1>
    <p class="page-subtitle">Overview of your tablet configuration</p>
  </header>

  {#if tabletsStore.hasTablet && tabletsStore.current}
    <div class="dashboard-grid">
      <TabletCard tablet={tabletsStore.current} />

      <GlassPanel class="quick-info">
        <h4 class="info-title">Output Mode</h4>
        <div class="info-value">
          {settingsStore.activeProfile?.outputMode?.name ?? 'Absolute Mode'}
        </div>
        <div class="info-detail">
          Maps tablet area to a region of your display
        </div>
      </GlassPanel>

      <GlassPanel class="quick-info">
        <h4 class="info-title">Quick Actions</h4>
        <div class="actions">
          <a href="#/area" class="action-link glass glass-interactive">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><rect x="3" y="3" width="18" height="18" rx="2"/><rect x="7" y="7" width="6" height="6" rx="1" opacity="0.6"/></svg>
            Configure Area
          </a>
          <a href="#/bindings" class="action-link glass glass-interactive">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>
            Edit Bindings
          </a>
        </div>
      </GlassPanel>
    </div>
  {:else}
    <div class="empty-state">
      <GlassPanel padding="var(--space-10)">
        <div class="empty-content">
          <div class="empty-icon">
            <svg width="64" height="64" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1" stroke-linecap="round" stroke-linejoin="round">
              <rect x="4" y="2" width="16" height="20" rx="2"/>
              <line x1="12" y1="18" x2="12.01" y2="18"/>
            </svg>
          </div>
          <h2 class="empty-title">No Tablet Detected</h2>
          <p class="empty-text">Connect a drawing tablet to get started. Make sure OpenTabletDriver daemon is running.</p>
          <div class="empty-pulse"></div>
        </div>
      </GlassPanel>
    </div>
  {/if}
</div>

<style>
  .dashboard {
    max-width: 900px;
  }

  .page-header {
    margin-bottom: var(--space-7);
  }

  .page-title {
    font-size: var(--font-size-2xl);
    font-weight: var(--font-weight-bold);
    color: var(--text-primary);
    margin: 0 0 var(--space-1) 0;
  }

  .page-subtitle {
    font-size: var(--font-size-base);
    color: var(--text-secondary);
    margin: 0;
  }

  .dashboard-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-5);
  }

  .dashboard-grid :global(.tablet-card) {
    grid-column: 1 / -1;
  }

  .info-title {
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-semibold);
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin: 0 0 var(--space-3) 0;
  }

  .info-value {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin-bottom: var(--space-1);
  }

  .info-detail {
    font-size: var(--font-size-sm);
    color: var(--text-secondary);
  }

  .actions {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }

  .action-link {
    display: flex;
    align-items: center;
    gap: var(--space-3);
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-md);
    color: var(--text-primary);
    text-decoration: none;
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
  }

  .empty-state {
    display: flex;
    justify-content: center;
    padding-top: var(--space-10);
  }

  .empty-content {
    display: flex;
    flex-direction: column;
    align-items: center;
    text-align: center;
    gap: var(--space-4);
  }

  .empty-icon {
    opacity: 0.5;
    animation: float 3s ease-in-out infinite;
  }

  .empty-title {
    font-size: var(--font-size-xl);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0;
  }

  .empty-text {
    font-size: var(--font-size-base);
    color: var(--text-secondary);
    max-width: 360px;
    margin: 0;
  }

  .empty-pulse {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: var(--warning);
    animation: pulse 2s ease-in-out infinite;
  }

  @keyframes float {
    0%, 100% { transform: translateY(0); }
    50% { transform: translateY(-8px); }
  }

  @keyframes pulse {
    0%, 100% { opacity: 1; box-shadow: 0 0 0 0 var(--warning); }
    50% { opacity: 0.6; box-shadow: 0 0 0 8px transparent; }
  }
</style>
