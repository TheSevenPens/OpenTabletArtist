<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import PageHeader from '../components/shared/PageHeader.svelte';
  import Placeholder from '../components/shared/Placeholder.svelte';
  import Dialog from '../components/shared/Dialog.svelte';
  import type { Preset } from '../types/presets';
  import { fetchAppInfo, openFolder } from '../services/api';
  import { pluralize } from '../utils/plugin';

  let presets = $state<Preset[]>([]);
  let loading = $state(true);
  let selectedPreset = $state<Preset | null>(null);
  let activeTab = $state<'general' | 'json'>('general');
  let presetsDir = $state<string | null>(null);
  let confirmDelete = $state<Preset | null>(null);
  let showSaveDialog = $state(false);
  let saveName = $state('');
  let saveError = $state<string | null>(null);
  let saving = $state(false);

  $effect(() => { loadPresets(); });

  async function loadPresets() {
    loading = true;
    try {
      const [presetsRes, appInfo] = await Promise.all([
        fetch('/api/presets').then(r => r.json()),
        fetchAppInfo(),
      ]);
      presets = presetsRes;
      presetsDir = appInfo.presetDirectory ?? appInfo.PresetDirectory ?? null;
    } catch {
      presets = [];
    }
    loading = false;
  }

  function parsePreset(content: string): any {
    try { return JSON.parse(content); } catch { return null; }
  }

  async function handleLoad(preset: Preset) {
    const res = await fetch('/api/presets/load', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: preset.name }),
    });
    if (res.ok) {
      const { fetchSettings } = await import('../services/api');
      const { settingsStore } = await import('../stores/settings.svelte');
      settingsStore.set(await fetchSettings());
    }
  }

  async function handleDelete(preset: Preset) {
    const res = await fetch('/api/presets/delete', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: preset.name }),
    });
    if (res.ok) {
      confirmDelete = null;
      await loadPresets();
    }
  }

  async function handleSave() {
    if (!saveName.trim()) { saveError = 'Please enter a name'; return; }
    saving = true;
    saveError = null;
    try {
      const res = await fetch('/api/presets/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: saveName.trim() }),
      });
      const data = await res.json();
      if (!res.ok) { saveError = data.error ?? 'Failed to save'; }
      else { showSaveDialog = false; await loadPresets(); }
    } catch { saveError = 'Failed to save snapshot'; }
    saving = false;
  }
</script>

<div class="page">
  <PageHeader title="Settings Snapshots" subtitle="Saved configuration snapshots">
    {#snippet actions()}
      <div class="header-actions">
        <button class="action-header-btn accent glass glass-interactive" onclick={() => { saveName = ''; saveError = null; showSaveDialog = true; }}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/></svg>
          Save Snapshot
        </button>
        {#if presetsDir}
          <button class="action-header-btn glass glass-interactive" onclick={() => presetsDir && openFolder(presetsDir)}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/></svg>
            Open Folder
          </button>
        {/if}
      </div>
    {/snippet}
  </PageHeader>

  {#if loading}
    <Placeholder title="Loading..." description="Loading snapshots..." />
  {:else if presets.length}
    <div class="list">
      {#each presets as preset}
        {@const parsed = parsePreset(preset.content)}
        <GlassPanel interactive>
          <div class="card">
            <div class="card-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>
            </div>
            <div class="card-info">
              <h3 class="card-name">{preset.name}</h3>
              {#if parsed?.Profiles}
                <span class="card-meta">{parsed.Profiles.length} {pluralize(parsed.Profiles.length, 'tablet')}</span>
              {/if}
            </div>
            <div class="card-actions">
              <button class="action-btn load glass glass-interactive" onclick={() => handleLoad(preset)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/></svg>
                Load
              </button>
              <button class="action-btn glass glass-interactive" onclick={() => { selectedPreset = preset; activeTab = 'general'; }}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><polyline points="14 2 14 8 20 8"/></svg>
                Open
              </button>
              <button class="action-btn danger glass glass-interactive" onclick={() => confirmDelete = preset}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/></svg>
                Delete
              </button>
            </div>
          </div>
        </GlassPanel>
      {/each}
    </div>
  {:else}
    <Placeholder title="No Snapshots" description="Snapshots are saved copies of your full configuration. Create them with the Save Snapshot button above.">
      {#snippet icon()}
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5"><path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/><polyline points="17 21 17 13 7 13 7 21"/><polyline points="7 3 7 8 15 8"/></svg>
      {/snippet}
    </Placeholder>
  {/if}
</div>

<!-- View snapshot dialog -->
{#if selectedPreset}
  {@const parsed = parsePreset(selectedPreset.content)}
  <Dialog title={selectedPreset.name} onclose={() => selectedPreset = null} wide>
    <div class="dialog-tabs">
      <button class="tab" class:tab-active={activeTab === 'general'} onclick={() => activeTab = 'general'}>General</button>
      <button class="tab" class:tab-active={activeTab === 'json'} onclick={() => activeTab = 'json'}>JSON</button>
    </div>
    <div class="dialog-body">
      {#if activeTab === 'general' && parsed}
        <div class="general-view">
          <section>
            <h4 class="section-title">Tablets in this Snapshot</h4>
            {#if parsed.Profiles?.length}
              <div class="profiles-list">
                {#each parsed.Profiles as profile}
                  <div class="kv-card glass-subtle">
                    <div class="kv-card-header">
                      <span class="kv-card-name">{profile.Tablet}</span>
                      <span class="kv-card-mode">{profile.OutputMode?.Path?.split('.').pop() ?? 'No mode'}</span>
                    </div>
                    {#if profile.AbsoluteModeSettings}
                      <div class="kv-card-details">
                        <span>Display: {profile.AbsoluteModeSettings.Display?.Width?.toFixed(0)} x {profile.AbsoluteModeSettings.Display?.Height?.toFixed(0)}</span>
                        <span>Tablet: {profile.AbsoluteModeSettings.Tablet?.Width?.toFixed(1)} x {profile.AbsoluteModeSettings.Tablet?.Height?.toFixed(1)} mm</span>
                      </div>
                    {/if}
                  </div>
                {/each}
              </div>
            {:else}
              <span class="empty-note">No tablet configurations in this snapshot</span>
            {/if}
          </section>
          <section>
            <h4 class="section-title">Global Settings</h4>
            <div class="kv-grid">
              <div class="kv"><span class="kv-key">Lock Display Area</span><span class="kv-val">{parsed.LockUsableAreaDisplay ? 'Yes' : 'No'}</span></div>
              <div class="kv"><span class="kv-key">Lock Tablet Area</span><span class="kv-val">{parsed.LockUsableAreaTablet ? 'Yes' : 'No'}</span></div>
            </div>
          </section>
        </div>
      {:else}
        <pre class="json-content">{JSON.stringify(parsed ?? selectedPreset.content, null, 2)}</pre>
      {/if}
    </div>
  </Dialog>
{/if}

<!-- Save dialog -->
{#if showSaveDialog}
  <Dialog title="Save Snapshot" onclose={() => showSaveDialog = false}>
    <div class="dialog-body">
      <p class="description">Save the current configuration as a snapshot. This captures all tablet settings, bindings, and filters.</p>
      <label class="save-field">
        <span class="save-label">Snapshot name</span>
        <!-- svelte-ignore a11y_autofocus -->
        <input type="text" class="save-input glass-subtle" placeholder="e.g. Drawing in Photoshop" bind:value={saveName} onkeydown={(e) => { if (e.key === 'Enter') handleSave(); if (e.key === 'Escape') showSaveDialog = false; }} autofocus />
      </label>
      {#if saveError}<p class="error-text">{saveError}</p>{/if}
      <div class="dialog-actions">
        <button class="btn-cancel glass glass-interactive" onclick={() => showSaveDialog = false}>Cancel</button>
        <button class="btn-confirm glass glass-interactive" onclick={handleSave} disabled={saving || !saveName.trim()}>{saving ? 'Saving...' : 'Save'}</button>
      </div>
    </div>
  </Dialog>
{/if}

<!-- Delete confirmation -->
{#if confirmDelete}
  <Dialog title="Delete Snapshot" onclose={() => confirmDelete = null}>
    <div class="dialog-body">
      <p class="description">Are you sure you want to delete "<strong>{confirmDelete.name}</strong>"? This cannot be undone.</p>
      <div class="dialog-actions">
        <button class="btn-cancel glass glass-interactive" onclick={() => confirmDelete = null}>Cancel</button>
        <button class="btn-confirm btn-danger glass glass-interactive" onclick={() => handleDelete(confirmDelete!)}>Delete</button>
      </div>
    </div>
  </Dialog>
{/if}

<style>
  .page { max-width: 900px; }

  .header-actions { display: flex; gap: var(--space-2); flex-shrink: 0; }

  .action-header-btn {
    display: flex; align-items: center; gap: var(--space-2);
    padding: var(--space-2) var(--space-4); border-radius: var(--radius-sm);
    font-size: var(--font-size-sm); font-weight: var(--font-weight-medium);
    color: var(--text-secondary); white-space: nowrap; flex-shrink: 0;
  }
  .action-header-btn:hover { color: var(--text-primary); }
  .action-header-btn.accent { border-color: var(--accent); color: var(--accent); }

  .list { display: flex; flex-direction: column; gap: var(--space-4); }

  .card { display: flex; align-items: center; gap: var(--space-4); }
  .card-icon { width: 48px; height: 48px; display: flex; align-items: center; justify-content: center; background: var(--accent-muted); color: var(--accent); border-radius: var(--radius-md); flex-shrink: 0; }
  .card-info { flex: 1; min-width: 0; }
  .card-name { font-size: var(--font-size-base); font-weight: var(--font-weight-semibold); color: var(--text-primary); margin: 0 0 var(--space-1) 0; }
  .card-meta { font-size: var(--font-size-xs); color: var(--text-muted); }
  .card-actions { display: flex; gap: var(--space-2); flex-shrink: 0; }

  .action-btn {
    display: flex; align-items: center; gap: var(--space-1);
    padding: var(--space-1) var(--space-3); border-radius: var(--radius-sm);
    font-size: var(--font-size-xs); font-weight: var(--font-weight-medium); color: var(--text-secondary);
  }
  .action-btn:hover { color: var(--text-primary); }
  .action-btn.load:hover { color: var(--accent); }
  .action-btn.danger:hover { color: var(--error); }

  /* Dialog content */
  .dialog-tabs { display: flex; gap: var(--space-1); padding: 0 var(--space-6); border-bottom: 1px solid var(--divider); }
  .tab { padding: var(--space-3) var(--space-4); font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); color: var(--text-muted); border-bottom: 2px solid transparent; margin-bottom: -1px; transition: all var(--transition-fast); }
  .tab:hover { color: var(--text-secondary); }
  .tab-active { color: var(--accent); border-bottom-color: var(--accent); }

  .dialog-body { padding: var(--space-5) var(--space-6); overflow-y: auto; flex: 1; }
  .json-content { font-family: var(--font-mono); font-size: var(--font-size-xs); color: var(--text-secondary); line-height: 1.6; white-space: pre-wrap; word-break: break-word; margin: 0; }
  .general-view { display: flex; flex-direction: column; gap: var(--space-6); }
  .section-title { font-size: var(--font-size-xs); font-weight: var(--font-weight-semibold); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; margin: 0 0 var(--space-3) 0; }
  .profiles-list { display: flex; flex-direction: column; gap: var(--space-3); }
  .kv-card { padding: var(--space-3) var(--space-4); border-radius: var(--radius-md); }
  .kv-card-header { display: flex; justify-content: space-between; align-items: center; margin-bottom: var(--space-2); }
  .kv-card-name { font-size: var(--font-size-sm); font-weight: var(--font-weight-semibold); color: var(--text-primary); }
  .kv-card-mode { font-size: var(--font-size-xs); color: var(--text-muted); font-family: var(--font-mono); }
  .kv-card-details { display: flex; gap: var(--space-5); font-size: var(--font-size-xs); color: var(--text-secondary); font-family: var(--font-mono); }
  .kv-grid { display: flex; flex-direction: column; gap: var(--space-2); }
  .kv { display: flex; justify-content: space-between; align-items: center; padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm); background: var(--glass-bg); }
  .kv-key { font-size: var(--font-size-sm); color: var(--text-secondary); }
  .kv-val { font-size: var(--font-size-sm); font-family: var(--font-mono); color: var(--text-primary); }
  .empty-note { font-size: var(--font-size-sm); color: var(--text-muted); font-style: italic; }

  /* Save/Delete dialog */
  .description { font-size: var(--font-size-sm); color: var(--text-secondary); margin: 0 0 var(--space-5) 0; line-height: var(--line-height-normal); }
  .save-field { display: flex; flex-direction: column; gap: var(--space-2); }
  .save-label { font-size: var(--font-size-xs); font-weight: var(--font-weight-medium); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; }
  .save-input { padding: var(--space-3) var(--space-4); border-radius: var(--radius-sm); background: var(--glass-bg); border: 1px solid var(--glass-border); color: var(--text-primary); font-size: var(--font-size-base); outline: none; transition: border-color var(--transition-fast); }
  .save-input:focus { border-color: var(--accent); }
  .save-input::placeholder { color: var(--text-muted); }
  .error-text { font-size: var(--font-size-xs); color: var(--error); margin: var(--space-2) 0 0 0; }
  .dialog-actions { display: flex; justify-content: flex-end; gap: var(--space-3); margin-top: var(--space-6); }
  .btn-cancel, .btn-confirm { padding: var(--space-2) var(--space-5); border-radius: var(--radius-sm); font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); }
  .btn-confirm { background: var(--accent-muted); border-color: var(--accent); color: var(--accent); }
  .btn-confirm:hover:not(:disabled) { background: var(--accent); color: var(--text-inverse); }
  .btn-confirm:disabled { opacity: 0.4; cursor: not-allowed; }
  .btn-danger { background: var(--error-muted); border-color: var(--error); color: var(--error); }
  .btn-danger:hover { background: var(--error); color: var(--text-inverse); }
</style>
