<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import type { Profile } from '../types/settings';
  import { settingsStore } from '../stores/settings.svelte';
  import { fetchAppInfo, openFolder } from '../services/api';

  let dialogProfile = $state<Profile | null>(null);
  let activeTab = $state<'general' | 'json'>('general');
  let settingsDir = $state<string | null>(null);
  let folderError = $state<string | null>(null);

  // Fetch settings directory on mount
  $effect(() => {
    fetchAppInfo().then(info => {
      // The settings file path gives us the directory
      const file = info.settingsFile ?? info.SettingsFile;
      if (file) {
        settingsDir = file.replace(/[/\\][^/\\]+$/, '');
      }
    }).catch(() => {});
  });

  async function handleOpenFolder() {
    if (!settingsDir) return;
    folderError = null;
    const result = await openFolder(settingsDir);
    if (result.error) folderError = result.error;
  }

  function openDialog(profile: Profile) {
    dialogProfile = profile;
    activeTab = 'general';
  }

  function closeDialog() {
    dialogProfile = null;
  }

  function handleBackdropClick(e: MouseEvent) {
    if (e.target === e.currentTarget) closeDialog();
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') closeDialog();
  }
</script>

<svelte:window onkeydown={handleKeydown} />

<div class="page">
  <header class="page-header">
    <h1 class="page-title">Tablet Settings</h1>
    <p class="page-subtitle">Manage per-tablet configurations</p>
  </header>

  {#if settingsStore.current?.profiles?.length}
    <div class="profiles-list">
      {#each settingsStore.current.profiles as profile}
        <GlassPanel interactive>
          <a href="#/tablets/{encodeURIComponent(profile.tablet)}/area" class="profile-card profile-link">
            <div class="profile-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <rect x="4" y="2" width="16" height="20" rx="2"/>
                <line x1="12" y1="18" x2="12.01" y2="18"/>
              </svg>
            </div>
            <div class="profile-info">
              <h3 class="profile-name">{profile.tablet}</h3>
              <span class="profile-mode">{profile.outputMode?.path?.split('.').pop() ?? 'No output mode'}</span>
            </div>
            <div class="profile-details">
              <div class="detail">
                <span class="detail-label">Display</span>
                <span class="detail-value">{profile.absoluteModeSettings?.display?.width?.toFixed(0)} x {profile.absoluteModeSettings?.display?.height?.toFixed(0)}</span>
              </div>
              <div class="detail">
                <span class="detail-label">Tablet</span>
                <span class="detail-value">{profile.absoluteModeSettings?.tablet?.width?.toFixed(1)} x {profile.absoluteModeSettings?.tablet?.height?.toFixed(1)} mm</span>
              </div>
            </div>
            <!-- svelte-ignore a11y_no_static_element_interactions -->
            <span class="open-btn glass glass-interactive" onclick={(e) => { e.preventDefault(); e.stopPropagation(); openDialog(profile); }}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
                <line x1="16" y1="13" x2="8" y2="13"/>
                <line x1="16" y1="17" x2="8" y2="17"/>
              </svg>
              Open
            </span>
          </a>
        </GlassPanel>
      {/each}
    </div>
  {:else}
    <GlassPanel>
      <div class="placeholder">
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
        </svg>
        <h3>No Profiles</h3>
        <p>Connect to the OTD daemon to view tablet profiles.</p>
      </div>
    </GlassPanel>
  {/if}
</div>

{#if dialogProfile}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="dialog-backdrop" onclick={handleBackdropClick}>
    <div class="dialog glass-heavy">
      <div class="dialog-header">
        <h2 class="dialog-title">{dialogProfile.tablet}</h2>
        <button class="dialog-close glass glass-interactive" onclick={closeDialog}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <line x1="18" y1="6" x2="6" y2="18"/>
            <line x1="6" y1="6" x2="18" y2="18"/>
          </svg>
        </button>
      </div>
      <div class="dialog-tabs">
        <button class="tab" class:tab-active={activeTab === 'general'} onclick={() => activeTab = 'general'}>General</button>
        <button class="tab" class:tab-active={activeTab === 'json'} onclick={() => activeTab = 'json'}>JSON</button>
      </div>
      <div class="dialog-body">
        {#if activeTab === 'general'}
          <div class="general-view">
            <!-- Output Mode -->
            <section class="section">
              <h4 class="section-title">Output Mode</h4>
              <div class="field-card glass-subtle">
                <span class="field-value">{dialogProfile.outputMode?.path?.split('.').pop() ?? 'None'}</span>
                <span class="field-hint">{dialogProfile.outputMode?.path ?? ''}</span>
              </div>
            </section>

            <!-- Absolute Mode -->
            {#if dialogProfile.absoluteModeSettings}
              <section class="section">
                <h4 class="section-title">Area Mapping</h4>
                <div class="area-grid">
                  <div class="area-card glass-subtle">
                    <div class="area-label">
                      <div class="area-dot" style="background: var(--accent)"></div>
                      Display
                    </div>
                    <div class="area-values">
                      <div class="area-row"><span>Size</span><span class="mono">{dialogProfile.absoluteModeSettings.display.width.toFixed(0)} x {dialogProfile.absoluteModeSettings.display.height.toFixed(0)}</span></div>
                      <div class="area-row"><span>Position</span><span class="mono">{dialogProfile.absoluteModeSettings.display.x.toFixed(0)}, {dialogProfile.absoluteModeSettings.display.y.toFixed(0)}</span></div>
                    </div>
                  </div>
                  <div class="area-card glass-subtle">
                    <div class="area-label">
                      <div class="area-dot" style="background: var(--success)"></div>
                      Tablet
                    </div>
                    <div class="area-values">
                      <div class="area-row"><span>Size</span><span class="mono">{dialogProfile.absoluteModeSettings.tablet.width.toFixed(1)} x {dialogProfile.absoluteModeSettings.tablet.height.toFixed(1)} mm</span></div>
                      <div class="area-row"><span>Position</span><span class="mono">{dialogProfile.absoluteModeSettings.tablet.x.toFixed(1)}, {dialogProfile.absoluteModeSettings.tablet.y.toFixed(1)}</span></div>
                    </div>
                  </div>
                </div>
                <div class="flags">
                  <span class="flag" class:flag-on={dialogProfile.absoluteModeSettings.enableClipping}>Clipping</span>
                  <span class="flag" class:flag-on={dialogProfile.absoluteModeSettings.enableAreaLimiting}>Area Limiting</span>
                  <span class="flag" class:flag-on={dialogProfile.absoluteModeSettings.lockAspectRatio}>Aspect Locked</span>
                </div>
              </section>
            {/if}

            <!-- Relative Mode -->
            {#if dialogProfile.relativeModeSettings}
              <section class="section">
                <h4 class="section-title">Relative Mode</h4>
                <div class="kv-grid">
                  <div class="kv"><span class="kv-key">X Sensitivity</span><span class="kv-val mono">{dialogProfile.relativeModeSettings.xSensitivity}</span></div>
                  <div class="kv"><span class="kv-key">Y Sensitivity</span><span class="kv-val mono">{dialogProfile.relativeModeSettings.ySensitivity}</span></div>
                  <div class="kv"><span class="kv-key">Reset Delay</span><span class="kv-val mono">{dialogProfile.relativeModeSettings.relativeResetDelay}</span></div>
                </div>
              </section>
            {/if}

            <!-- Bindings -->
            {#if dialogProfile.bindings}
              <section class="section">
                <h4 class="section-title">Bindings</h4>
                <div class="kv-grid">
                  <div class="kv">
                    <span class="kv-key">Tip</span>
                    <span class="kv-val mono">{dialogProfile.bindings.tipButton?.settings?.find(s => s.property === 'button')?.value ?? dialogProfile.bindings.tipButton?.path?.split('.').pop() ?? 'None'}</span>
                  </div>
                  <div class="kv">
                    <span class="kv-key">Tip Threshold</span>
                    <span class="kv-val mono">{(dialogProfile.bindings.tipActivationThreshold * 100).toFixed(0)}%</span>
                  </div>
                  <div class="kv">
                    <span class="kv-key">Eraser</span>
                    <span class="kv-val mono">{dialogProfile.bindings.eraserButton?.settings?.find(s => s.property === 'button')?.value ?? dialogProfile.bindings.eraserButton?.path?.split('.').pop() ?? 'None'}</span>
                  </div>
                  {#if dialogProfile.bindings.penButtons?.length}
                    <div class="kv">
                      <span class="kv-key">Pen Buttons</span>
                      <span class="kv-val mono">{dialogProfile.bindings.penButtons.length} configured</span>
                    </div>
                  {/if}
                  {#if dialogProfile.bindings.auxButtons?.length}
                    <div class="kv">
                      <span class="kv-key">Aux Buttons</span>
                      <span class="kv-val mono">{dialogProfile.bindings.auxButtons.length} configured</span>
                    </div>
                  {/if}
                </div>
              </section>
            {/if}

            <!-- Filters -->
            <section class="section">
              <h4 class="section-title">Filters</h4>
              {#if dialogProfile.filters?.length}
                <div class="kv-grid">
                  {#each dialogProfile.filters as filter}
                    <div class="kv">
                      <span class="kv-key">{filter.path?.split('.').pop()}</span>
                      <span class="kv-val mono">{filter.enable ? 'Enabled' : 'Disabled'}</span>
                    </div>
                  {/each}
                </div>
              {:else}
                <span class="empty-note">No filters configured</span>
              {/if}
            </section>
          </div>
        {:else}
          <pre class="json-content">{JSON.stringify(dialogProfile, null, 2)}</pre>
        {/if}
      </div>
    </div>
  </div>
{/if}

<style>
  .page { max-width: 900px; }
  .page-header { margin-bottom: var(--space-7); }
  .page-header-row { display: flex; align-items: flex-start; justify-content: space-between; gap: var(--space-4); }
  .page-title { font-size: var(--font-size-2xl); font-weight: var(--font-weight-bold); color: var(--text-primary); margin: 0 0 var(--space-1) 0; }
  .page-subtitle { font-size: var(--font-size-base); color: var(--text-secondary); margin: 0; }

  .folder-btn {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-4);
    border-radius: var(--radius-sm);
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    color: var(--text-secondary);
    white-space: nowrap;
    flex-shrink: 0;
  }

  .folder-btn:hover { color: var(--text-primary); }

  .folder-error {
    font-size: var(--font-size-xs);
    color: var(--error);
    margin: var(--space-2) 0 0 0;
  }

  .profiles-list {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }

  .profile-card {
    display: flex;
    align-items: center;
    gap: var(--space-4);
  }

  .profile-link {
    text-decoration: none;
    color: inherit;
  }

  .profile-icon {
    width: 48px;
    height: 48px;
    display: flex;
    align-items: center;
    justify-content: center;
    background: var(--accent-muted);
    color: var(--accent);
    border-radius: var(--radius-md);
    flex-shrink: 0;
  }

  .profile-info {
    flex: 1;
    min-width: 0;
  }

  .profile-name {
    font-size: var(--font-size-base);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0 0 var(--space-1) 0;
  }

  .profile-mode {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
  }

  .profile-details {
    display: flex;
    gap: var(--space-6);
    flex-shrink: 0;
  }

  .detail {
    display: flex;
    flex-direction: column;
    gap: var(--space-1);
    text-align: right;
  }

  .detail-label {
    font-size: 10px;
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }

  .detail-value {
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    color: var(--text-secondary);
    font-family: var(--font-mono);
  }

  .open-btn {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    padding: var(--space-2) var(--space-3);
    border-radius: var(--radius-sm);
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-medium);
    color: var(--text-secondary);
    flex-shrink: 0;
  }

  .open-btn:hover {
    color: var(--text-primary);
  }

  /* Dialog */
  .dialog-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.12);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 100;
    animation: fadeIn 0.15s ease;
  }

  .dialog {
    width: 90%;
    max-width: 700px;
    max-height: 80vh;
    display: flex;
    flex-direction: column;
    animation: scaleIn 0.2s cubic-bezier(0.34, 1.56, 0.64, 1);
  }

  .dialog-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-5) var(--space-6);
    border-bottom: 1px solid var(--divider);
  }

  .dialog-title {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0;
  }

  .dialog-close {
    padding: var(--space-2);
    border-radius: var(--radius-sm);
    color: var(--text-muted);
  }

  .dialog-close:hover {
    color: var(--text-primary);
  }

  .dialog-tabs {
    display: flex;
    gap: var(--space-1);
    padding: 0 var(--space-6);
    border-bottom: 1px solid var(--divider);
  }

  .tab {
    padding: var(--space-3) var(--space-4);
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
    color: var(--text-muted);
    border-bottom: 2px solid transparent;
    margin-bottom: -1px;
    transition: all var(--transition-fast);
  }

  .tab:hover { color: var(--text-secondary); }

  .tab-active {
    color: var(--accent);
    border-bottom-color: var(--accent);
  }

  .dialog-body {
    padding: var(--space-5) var(--space-6);
    overflow-y: auto;
    flex: 1;
  }

  .json-content {
    font-family: var(--font-mono);
    font-size: var(--font-size-xs);
    color: var(--text-secondary);
    line-height: 1.6;
    white-space: pre-wrap;
    word-break: break-word;
    margin: 0;
  }

  /* General view */
  .general-view {
    display: flex;
    flex-direction: column;
    gap: var(--space-6);
  }

  .section-title {
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-semibold);
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.5px;
    margin: 0 0 var(--space-3) 0;
  }

  .field-card {
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-md);
  }

  .field-value {
    display: block;
    font-size: var(--font-size-base);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin-bottom: var(--space-1);
  }

  .field-hint {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
    font-family: var(--font-mono);
  }

  .area-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-3);
  }

  .area-card {
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-md);
  }

  .area-label {
    display: flex;
    align-items: center;
    gap: var(--space-2);
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin-bottom: var(--space-3);
  }

  .area-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
  }

  .area-values {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }

  .area-row {
    display: flex;
    justify-content: space-between;
    font-size: var(--font-size-xs);
    color: var(--text-secondary);
  }

  .mono {
    font-family: var(--font-mono);
    color: var(--text-primary);
  }

  .flags {
    display: flex;
    gap: var(--space-2);
    margin-top: var(--space-3);
    flex-wrap: wrap;
  }

  .flag {
    padding: var(--space-1) var(--space-3);
    border-radius: var(--radius-full);
    font-size: 10px;
    font-weight: var(--font-weight-medium);
    text-transform: uppercase;
    letter-spacing: 0.3px;
    background: var(--glass-bg);
    border: 1px solid var(--glass-border);
    color: var(--text-muted);
  }

  .flag-on {
    background: var(--success-muted);
    border-color: var(--success);
    color: var(--success);
  }

  .kv-grid {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }

  .kv {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: var(--space-2) var(--space-3);
    border-radius: var(--radius-sm);
    background: var(--glass-bg);
  }

  .kv-key {
    font-size: var(--font-size-sm);
    color: var(--text-secondary);
  }

  .kv-val {
    font-size: var(--font-size-sm);
  }

  .empty-note {
    font-size: var(--font-size-sm);
    color: var(--text-muted);
    font-style: italic;
  }

  @keyframes fadeIn {
    from { opacity: 0; }
    to { opacity: 1; }
  }

  @keyframes scaleIn {
    from { opacity: 0; transform: scale(0.95); }
    to { opacity: 1; transform: scale(1); }
  }

  .placeholder { display: flex; flex-direction: column; align-items: center; text-align: center; gap: var(--space-4); padding: var(--space-10) 0; }
  .placeholder h3 { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-secondary); margin: 0; }
  .placeholder p { font-size: var(--font-size-sm); color: var(--text-muted); max-width: 320px; margin: 0; }
</style>
