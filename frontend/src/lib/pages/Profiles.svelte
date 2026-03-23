<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import PageHeader from '../components/shared/PageHeader.svelte';
  import Placeholder from '../components/shared/Placeholder.svelte';
  import Dialog from '../components/shared/Dialog.svelte';
  import type { Profile } from '../types/settings';
  import { settingsStore } from '../stores/settings.svelte';
  import { getPluginDisplayName, getPluginShortName } from '../utils/plugin';

  let dialogProfile = $state<Profile | null>(null);
  let activeTab = $state<'general' | 'json'>('general');

  function openDialog(profile: Profile) {
    dialogProfile = profile;
    activeTab = 'general';
  }
</script>

<div class="page">
  <PageHeader title="Tablet Settings" subtitle="Manage per-tablet configurations" />

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
              <span class="profile-mode">{getPluginShortName(profile.outputMode, 'No output mode')}</span>
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
            <button class="open-btn glass glass-interactive" onclick={(e) => { e.preventDefault(); e.stopPropagation(); openDialog(profile); }}>
              <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                <polyline points="14 2 14 8 20 8"/>
              </svg>
              Open
            </button>
          </a>
        </GlassPanel>
      {/each}
    </div>
  {:else}
    <Placeholder title="No Tablets" description="Connect to the OTD daemon to view tablet configurations.">
      {#snippet icon()}
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M17 21v-2a4 4 0 0 0-4-4H5a4 4 0 0 0-4 4v2"/><circle cx="9" cy="7" r="4"/><path d="M23 21v-2a4 4 0 0 0-3-3.87"/><path d="M16 3.13a4 4 0 0 1 0 7.75"/>
        </svg>
      {/snippet}
    </Placeholder>
  {/if}
</div>

{#if dialogProfile}
  <Dialog title={dialogProfile.tablet} onclose={() => dialogProfile = null} wide>
    <div class="dialog-tabs">
      <button class="tab" class:tab-active={activeTab === 'general'} onclick={() => activeTab = 'general'}>General</button>
      <button class="tab" class:tab-active={activeTab === 'json'} onclick={() => activeTab = 'json'}>JSON</button>
    </div>
    <div class="dialog-body">
      {#if activeTab === 'general'}
        <div class="general-view">
          <section class="section">
            <h4 class="section-title">Output Mode</h4>
            <div class="field-card glass-subtle">
              <span class="field-value">{getPluginShortName(dialogProfile.outputMode)}</span>
              <span class="field-hint">{dialogProfile.outputMode?.path ?? ''}</span>
            </div>
          </section>

          {#if dialogProfile.absoluteModeSettings}
            <section class="section">
              <h4 class="section-title">Area Mapping</h4>
              <div class="area-grid">
                <div class="area-card glass-subtle">
                  <div class="area-label"><div class="area-dot" style="background: var(--accent)"></div> Display</div>
                  <div class="area-values">
                    <div class="area-row"><span>Size</span><span class="mono">{dialogProfile.absoluteModeSettings.display.width.toFixed(0)} x {dialogProfile.absoluteModeSettings.display.height.toFixed(0)}</span></div>
                    <div class="area-row"><span>Position</span><span class="mono">{dialogProfile.absoluteModeSettings.display.x.toFixed(0)}, {dialogProfile.absoluteModeSettings.display.y.toFixed(0)}</span></div>
                  </div>
                </div>
                <div class="area-card glass-subtle">
                  <div class="area-label"><div class="area-dot" style="background: var(--success)"></div> Tablet</div>
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

          {#if dialogProfile.bindings}
            <section class="section">
              <h4 class="section-title">Bindings</h4>
              <div class="kv-grid">
                <div class="kv"><span class="kv-key">Tip</span><span class="kv-val mono">{getPluginDisplayName(dialogProfile.bindings.tipButton)}</span></div>
                <div class="kv"><span class="kv-key">Tip Threshold</span><span class="kv-val mono">{(dialogProfile.bindings.tipActivationThreshold * 100).toFixed(0)}%</span></div>
                <div class="kv"><span class="kv-key">Eraser</span><span class="kv-val mono">{getPluginDisplayName(dialogProfile.bindings.eraserButton)}</span></div>
                {#if dialogProfile.bindings.penButtons?.length}
                  <div class="kv"><span class="kv-key">Pen Buttons</span><span class="kv-val mono">{dialogProfile.bindings.penButtons.length} configured</span></div>
                {/if}
                {#if dialogProfile.bindings.auxButtons?.length}
                  <div class="kv"><span class="kv-key">Aux Buttons</span><span class="kv-val mono">{dialogProfile.bindings.auxButtons.length} configured</span></div>
                {/if}
              </div>
            </section>
          {/if}

          <section class="section">
            <h4 class="section-title">Filters</h4>
            {#if dialogProfile.filters?.length}
              <div class="kv-grid">
                {#each dialogProfile.filters as filter}
                  <div class="kv"><span class="kv-key">{getPluginShortName(filter)}</span><span class="kv-val mono">{filter.enable ? 'Enabled' : 'Disabled'}</span></div>
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
  </Dialog>
{/if}

<style>
  .page { max-width: 900px; }

  .profiles-list { display: flex; flex-direction: column; gap: var(--space-4); }

  .profile-card { display: flex; align-items: center; gap: var(--space-4); }
  .profile-link { text-decoration: none; color: inherit; }

  .profile-icon {
    width: 48px; height: 48px;
    display: flex; align-items: center; justify-content: center;
    background: var(--accent-muted); color: var(--accent);
    border-radius: var(--radius-md); flex-shrink: 0;
  }

  .profile-info { flex: 1; min-width: 0; }
  .profile-name { font-size: var(--font-size-base); font-weight: var(--font-weight-semibold); color: var(--text-primary); margin: 0 0 var(--space-1) 0; }
  .profile-mode { font-size: var(--font-size-xs); color: var(--text-muted); }

  .profile-details { display: flex; gap: var(--space-6); flex-shrink: 0; }
  .detail { display: flex; flex-direction: column; gap: var(--space-1); text-align: right; }
  .detail-label { font-size: 10px; color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; }
  .detail-value { font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); color: var(--text-secondary); font-family: var(--font-mono); }

  .open-btn {
    display: flex; align-items: center; gap: var(--space-2);
    padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm);
    font-size: var(--font-size-xs); font-weight: var(--font-weight-medium);
    color: var(--text-secondary); flex-shrink: 0;
  }
  .open-btn:hover { color: var(--text-primary); }

  /* Dialog content */
  .dialog-tabs { display: flex; gap: var(--space-1); padding: 0 var(--space-6); border-bottom: 1px solid var(--divider); }
  .tab { padding: var(--space-3) var(--space-4); font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); color: var(--text-muted); border-bottom: 2px solid transparent; margin-bottom: -1px; transition: all var(--transition-fast); }
  .tab:hover { color: var(--text-secondary); }
  .tab-active { color: var(--accent); border-bottom-color: var(--accent); }

  .dialog-body { padding: var(--space-5) var(--space-6); overflow-y: auto; flex: 1; }

  .json-content { font-family: var(--font-mono); font-size: var(--font-size-xs); color: var(--text-secondary); line-height: 1.6; white-space: pre-wrap; word-break: break-word; margin: 0; }

  .general-view { display: flex; flex-direction: column; gap: var(--space-6); }
  .section-title { font-size: var(--font-size-xs); font-weight: var(--font-weight-semibold); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; margin: 0 0 var(--space-3) 0; }

  .field-card { padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); }
  .field-value { display: block; font-size: var(--font-size-base); font-weight: var(--font-weight-semibold); color: var(--text-primary); margin-bottom: var(--space-1); }
  .field-hint { font-size: var(--font-size-xs); color: var(--text-muted); font-family: var(--font-mono); }

  .area-grid { display: grid; grid-template-columns: 1fr 1fr; gap: var(--space-3); }
  .area-card { padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); }
  .area-label { display: flex; align-items: center; gap: var(--space-2); font-size: var(--font-size-sm); font-weight: var(--font-weight-semibold); color: var(--text-primary); margin-bottom: var(--space-3); }
  .area-dot { width: 8px; height: 8px; border-radius: 50%; }
  .area-values { display: flex; flex-direction: column; gap: var(--space-2); }
  .area-row { display: flex; justify-content: space-between; font-size: var(--font-size-xs); color: var(--text-secondary); }
  .mono { font-family: var(--font-mono); color: var(--text-primary); }

  .flags { display: flex; gap: var(--space-2); margin-top: var(--space-3); flex-wrap: wrap; }
  .flag { padding: var(--space-1) var(--space-3); border-radius: var(--radius-full); font-size: 10px; font-weight: var(--font-weight-medium); text-transform: uppercase; letter-spacing: 0.3px; background: var(--glass-bg); border: 1px solid var(--glass-border); color: var(--text-muted); }
  .flag-on { background: var(--success-muted); border-color: var(--success); color: var(--success); }

  .kv-grid { display: flex; flex-direction: column; gap: var(--space-2); }
  .kv { display: flex; justify-content: space-between; align-items: center; padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm); background: var(--glass-bg); }
  .kv-key { font-size: var(--font-size-sm); color: var(--text-secondary); }
  .kv-val { font-size: var(--font-size-sm); }
  .empty-note { font-size: var(--font-size-sm); color: var(--text-muted); font-style: italic; }
</style>
