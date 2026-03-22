<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import TabletCard from '../components/tablet/TabletCard.svelte';
  import { tabletsStore } from '../stores/tablets.svelte';
  import { connectionStore } from '../stores/connection.svelte';
  import { settingsStore } from '../stores/settings.svelte';
  import { vmultiStore } from '../stores/vmulti.svelte';

  // Detect Windows Ink plugin from the active profile's output mode.
  // The daemon's GetSettings() returns profile.OutputMode as a PluginSettingStore
  // with a Path (e.g. "VoiDPlugins.OutputMode.WinInkAbsoluteMode") and Name
  // (e.g. "Windows Ink Absolute Mode"). The IDriverDaemon interface doesn't expose
  // a "list installed plugins" method, so we infer from the active output mode.
  // Future: scan AppInfo.PluginDirectory for installed plugin DLLs via the bridge.
  let hasWindowsInk = $derived(
    settingsStore.activeProfile?.outputMode?.path?.toLowerCase().includes('winink') ?? false
  );
</script>

<div class="dashboard">
  <header class="page-header">
    <h1 class="page-title">Dashboard</h1>
    <p class="page-subtitle">Overview of your tablet configuration</p>
  </header>

  <div class="status-cards">
    <!-- OTD Daemon Status -->
    <GlassPanel interactive>
      <div class="status-card">
        <div class="status-icon" class:active={connectionStore.isConnected}>
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="2" y="2" width="20" height="8" rx="2" ry="2"/>
            <rect x="2" y="14" width="20" height="8" rx="2" ry="2"/>
            <line x1="6" y1="6" x2="6.01" y2="6"/>
            <line x1="6" y1="18" x2="6.01" y2="18"/>
          </svg>
        </div>
        <div class="status-info">
          <h3 class="status-label">OpenTabletDriver</h3>
          <div class="status-row">
            <div class="status-dot" class:dot-connected={connectionStore.isConnected} class:dot-disconnected={!connectionStore.isConnected}></div>
            <span class="status-text" class:text-ok={connectionStore.isConnected}>
              {connectionStore.isConnected ? 'Daemon running' : 'Not connected'}
            </span>
          </div>
        </div>
      </div>
    </GlassPanel>

    <!-- Tablet Status -->
    <GlassPanel interactive>
      <div class="status-card">
        <div class="status-icon" class:active={tabletsStore.hasTablet}>
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <rect x="4" y="2" width="16" height="20" rx="2"/>
            <line x1="12" y1="18" x2="12.01" y2="18"/>
          </svg>
        </div>
        <div class="status-info">
          <h3 class="status-label">Tablet</h3>
          <div class="status-row">
            <div class="status-dot" class:dot-connected={tabletsStore.hasTablet} class:dot-disconnected={!tabletsStore.hasTablet}></div>
            <span class="status-text" class:text-ok={tabletsStore.hasTablet}>
              {tabletsStore.hasTablet ? tabletsStore.current?.name : 'No tablet detected'}
            </span>
          </div>
        </div>
      </div>
    </GlassPanel>

    <!-- VMulti Driver Status -->
    <GlassPanel interactive>
      <div class="status-card">
        <div class="status-icon" class:active={vmultiStore.isInstalled}>
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>
          </svg>
        </div>
        <div class="status-info">
          <h3 class="status-label">VMulti Driver</h3>
          <div class="status-row">
            <div class="status-dot" class:dot-connected={vmultiStore.isInstalled} class:dot-disconnected={!vmultiStore.isInstalled}></div>
            <span class="status-text" class:text-ok={vmultiStore.isInstalled}>
              {vmultiStore.message}
            </span>
          </div>
          {#if !vmultiStore.isInstalled}
            <span class="status-hint">Required for pressure &amp; tilt</span>
          {/if}
        </div>
      </div>
    </GlassPanel>

    <!-- Windows Ink Plugin Status -->
    <GlassPanel interactive>
      <div class="status-card">
        <div class="status-icon" class:active={hasWindowsInk}>
          <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M17 3a2.85 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5Z"/>
            <path d="m15 5 4 4"/>
          </svg>
        </div>
        <div class="status-info">
          <h3 class="status-label">Windows Ink</h3>
          <div class="status-row">
            <div class="status-dot" class:dot-connected={hasWindowsInk} class:dot-disconnected={!hasWindowsInk}></div>
            <span class="status-text" class:text-ok={hasWindowsInk}>
              {hasWindowsInk ? 'Plugin active' : 'Not configured'}
            </span>
          </div>
          {#if !hasWindowsInk}
            <span class="status-hint">Enables pressure in drawing apps</span>
          {/if}
        </div>
      </div>
    </GlassPanel>
  </div>

  {#if tabletsStore.hasTablet && tabletsStore.current}
    <div class="dashboard-grid">
      <TabletCard tablet={tabletsStore.current} />

      <GlassPanel class="quick-info">
        <h4 class="info-title">Output Mode</h4>
        <div class="info-value">
          {settingsStore.activeProfile?.outputMode?.path?.split('.').pop() ?? 'Absolute Mode'}
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

  .status-cards {
    display: flex;
    flex-direction: column;
    gap: var(--space-3);
    margin-bottom: var(--space-7);
  }

  .status-card {
    display: flex;
    align-items: center;
    gap: var(--space-4);
  }

  .status-icon {
    width: 48px;
    height: 48px;
    display: flex;
    align-items: center;
    justify-content: center;
    border-radius: var(--radius-md);
    background: var(--glass-bg);
    color: var(--text-muted);
    flex-shrink: 0;
    transition: all var(--transition-smooth);
  }

  .status-icon.active {
    background: var(--success-muted);
    color: var(--success);
  }

  .status-info {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
    min-width: 0;
  }

  .status-label {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0;
  }

  .status-row {
    display: flex;
    align-items: center;
    gap: var(--space-2);
  }

  .status-dot {
    width: 6px;
    height: 6px;
    border-radius: 50%;
    flex-shrink: 0;
  }

  .dot-connected {
    background: var(--success);
    box-shadow: 0 0 6px var(--success);
  }

  .dot-disconnected {
    background: var(--text-muted);
  }

  .status-text {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
  }

  .status-text.text-ok {
    color: var(--text-secondary);
  }

  .status-hint {
    font-size: 10px;
    color: var(--text-muted);
    font-style: italic;
    margin-top: calc(-1 * var(--space-1));
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
</style>
