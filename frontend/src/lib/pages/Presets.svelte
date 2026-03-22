<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import { fetchAppInfo, openFolder } from '../services/api';

  interface Preset {
    name: string;
    path: string;
    content: string;
  }

  let presets = $state<Preset[]>([]);
  let loading = $state(true);
  let selectedPreset = $state<Preset | null>(null);
  let activeTab = $state<'general' | 'json'>('general');
  let presetsDir = $state<string | null>(null);

  $effect(() => {
    loadPresets();
  });

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

  function openPreset(preset: Preset) {
    selectedPreset = preset;
    activeTab = 'general';
  }

  function closeDialog() {
    selectedPreset = null;
  }

  function handleBackdropClick(e: MouseEvent) {
    if (e.target === e.currentTarget) closeDialog();
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') closeDialog();
  }

  function parsePreset(content: string): any {
    try { return JSON.parse(content); } catch { return null; }
  }

  let confirmDelete = $state<Preset | null>(null);

  async function handleLoad(preset: Preset) {
    const res = await fetch('/api/presets/load', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ name: preset.name }),
    });
    if (res.ok) {
      // Re-fetch settings since we just applied new ones
      const { fetchSettings } = await import('../services/api');
      const { settingsStore } = await import('../stores/settings.svelte');
      const settings = await fetchSettings();
      settingsStore.set(settings);
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

  async function handleOpenFolder() {
    if (presetsDir) await openFolder(presetsDir);
  }

  let showSaveDialog = $state(false);
  let saveName = $state('');
  let saveError = $state<string | null>(null);
  let saving = $state(false);

  function openSaveDialog() {
    saveName = '';
    saveError = null;
    showSaveDialog = true;
  }

  function closeSaveDialog() {
    showSaveDialog = false;
  }

  async function handleSave() {
    if (!saveName.trim()) {
      saveError = 'Please enter a name';
      return;
    }
    saving = true;
    saveError = null;
    try {
      const res = await fetch('/api/presets/save', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ name: saveName.trim() }),
      });
      const data = await res.json();
      if (!res.ok) {
        saveError = data.error ?? 'Failed to save';
      } else {
        closeSaveDialog();
        await loadPresets();
      }
    } catch {
      saveError = 'Failed to save snapshot';
    }
    saving = false;
  }

  function handleSaveKeydown(e: KeyboardEvent) {
    if (e.key === 'Enter') handleSave();
    if (e.key === 'Escape') closeSaveDialog();
  }
</script>

<svelte:window onkeydown={handleKeydown} />

<div class="page">
  <header class="page-header">
    <div class="page-header-row">
      <div>
        <h1 class="page-title">Settings Snapshots</h1>
        <p class="page-subtitle">Saved configuration snapshots</p>
      </div>
      <div class="header-actions">
        <button class="save-btn glass glass-interactive" onclick={openSaveDialog}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <line x1="12" y1="5" x2="12" y2="19"/><line x1="5" y1="12" x2="19" y2="12"/>
          </svg>
          Save Snapshot
        </button>
        {#if presetsDir}
          <button class="folder-btn glass glass-interactive" onclick={handleOpenFolder}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
              <path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"/>
            </svg>
            Open Folder
          </button>
        {/if}
      </div>
    </div>
  </header>

  {#if loading}
    <GlassPanel>
      <div class="placeholder">
        <p>Loading snapshots...</p>
      </div>
    </GlassPanel>
  {:else if presets.length}
    <div class="presets-list">
      {#each presets as preset}
        <GlassPanel interactive>
          <div class="preset-card">
            <div class="preset-icon">
              <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
                <polyline points="17 21 17 13 7 13 7 21"/>
                <polyline points="7 3 7 8 15 8"/>
              </svg>
            </div>
            <div class="preset-info">
              <h3 class="preset-name">{preset.name}</h3>
              {#if parsePreset(preset.content)?.Profiles}
                <span class="preset-meta">{parsePreset(preset.content).Profiles.length} profile{parsePreset(preset.content).Profiles.length !== 1 ? 's' : ''}</span>
              {/if}
            </div>
            <div class="card-actions">
              <button class="action-btn load-btn glass glass-interactive" onclick={() => handleLoad(preset)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="1 4 1 10 7 10"/><path d="M3.51 15a9 9 0 1 0 2.13-9.36L1 10"/>
                </svg>
                Load
              </button>
              <button class="action-btn open-btn glass glass-interactive" onclick={() => openPreset(preset)}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
                  <polyline points="14 2 14 8 20 8"/>
                </svg>
                Open
              </button>
              <button class="action-btn delete-btn glass glass-interactive" onclick={() => confirmDelete = preset}>
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
                  <polyline points="3 6 5 6 21 6"/><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"/>
                </svg>
                Delete
              </button>
            </div>
          </div>
        </GlassPanel>
      {/each}
    </div>
  {:else}
    <GlassPanel>
      <div class="placeholder">
        <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
          <path d="M19 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h11l5 5v11a2 2 0 0 1-2 2z"/>
          <polyline points="17 21 17 13 7 13 7 21"/>
          <polyline points="7 3 7 8 15 8"/>
        </svg>
        <h3>No Snapshots</h3>
        <p>Snapshots are saved copies of your full configuration. Create them in the OTD UX to see them here.</p>
      </div>
    </GlassPanel>
  {/if}
</div>

{#if selectedPreset}
  {@const parsed = parsePreset(selectedPreset.content)}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="dialog-backdrop" onclick={handleBackdropClick}>
    <div class="dialog glass-heavy">
      <div class="dialog-header">
        <h2 class="dialog-title">{selectedPreset.name}</h2>
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
        {#if activeTab === 'general' && parsed}
          <div class="general-view">
            <section class="section">
              <h4 class="section-title">Tablets in this Snapshot</h4>
              {#if parsed.Profiles?.length}
                <div class="preset-profiles">
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

            <section class="section">
              <h4 class="section-title">Global Settings</h4>
              <div class="kv-grid">
                <div class="kv"><span class="kv-key">Lock Display Area</span><span class="kv-val">{parsed.LockUsableAreaDisplay ? 'Yes' : 'No'}</span></div>
                <div class="kv"><span class="kv-key">Lock Tablet Area</span><span class="kv-val">{parsed.LockUsableAreaTablet ? 'Yes' : 'No'}</span></div>
                {#if parsed.Tools?.length}
                  <div class="kv"><span class="kv-key">Tools</span><span class="kv-val">{parsed.Tools.length} configured</span></div>
                {/if}
              </div>
            </section>
          </div>
        {:else}
          <pre class="json-content">{JSON.stringify(parsed ?? selectedPreset.content, null, 2)}</pre>
        {/if}
      </div>
    </div>
  </div>
{/if}

{#if showSaveDialog}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="dialog-backdrop" onclick={(e) => { if (e.target === e.currentTarget) closeSaveDialog(); }}>
    <div class="dialog glass-heavy save-dialog">
      <div class="dialog-header">
        <h2 class="dialog-title">Save Snapshot</h2>
        <button class="dialog-close glass glass-interactive" onclick={closeSaveDialog}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <line x1="18" y1="6" x2="6" y2="18"/>
            <line x1="6" y1="6" x2="18" y2="18"/>
          </svg>
        </button>
      </div>
      <div class="dialog-body">
        <p class="save-description">Save the current configuration as a snapshot. This captures all tablet settings, bindings, and filters.</p>
        <label class="save-field">
          <span class="save-label">Snapshot name</span>
          <!-- svelte-ignore a11y_autofocus -->
          <input
            type="text"
            class="save-input glass-subtle"
            placeholder="e.g. Drawing in Photoshop"
            bind:value={saveName}
            onkeydown={handleSaveKeydown}
            autofocus
          />
        </label>
        {#if saveError}
          <p class="save-error">{saveError}</p>
        {/if}
        <div class="save-actions">
          <button class="cancel-btn glass glass-interactive" onclick={closeSaveDialog}>Cancel</button>
          <button class="confirm-btn glass glass-interactive" onclick={handleSave} disabled={saving || !saveName.trim()}>
            {saving ? 'Saving...' : 'Save'}
          </button>
        </div>
      </div>
    </div>
  </div>
{/if}

{#if confirmDelete}
  <!-- svelte-ignore a11y_no_static_element_interactions -->
  <div class="dialog-backdrop" onclick={(e) => { if (e.target === e.currentTarget) confirmDelete = null; }}>
    <div class="dialog glass-heavy save-dialog">
      <div class="dialog-header">
        <h2 class="dialog-title">Delete Snapshot</h2>
        <button class="dialog-close glass glass-interactive" onclick={() => confirmDelete = null}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <line x1="18" y1="6" x2="6" y2="18"/>
            <line x1="6" y1="6" x2="18" y2="18"/>
          </svg>
        </button>
      </div>
      <div class="dialog-body">
        <p class="save-description">Are you sure you want to delete "<strong>{confirmDelete.name}</strong>"? This cannot be undone.</p>
        <div class="save-actions">
          <button class="cancel-btn glass glass-interactive" onclick={() => confirmDelete = null}>Cancel</button>
          <button class="confirm-btn delete-confirm-btn glass glass-interactive" onclick={() => handleDelete(confirmDelete!)}>Delete</button>
        </div>
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

  .header-actions {
    display: flex;
    gap: var(--space-2);
    flex-shrink: 0;
  }

  .save-btn, .folder-btn {
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
  .save-btn:hover, .folder-btn:hover { color: var(--text-primary); }

  .save-btn {
    border-color: var(--accent);
    color: var(--accent);
  }

  .presets-list {
    display: flex;
    flex-direction: column;
    gap: var(--space-4);
  }

  .preset-card {
    display: flex;
    align-items: center;
    gap: var(--space-4);
  }

  .preset-icon {
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

  .preset-info { flex: 1; min-width: 0; }
  .preset-name { font-size: var(--font-size-base); font-weight: var(--font-weight-semibold); color: var(--text-primary); margin: 0 0 var(--space-1) 0; }
  .preset-meta { font-size: var(--font-size-xs); color: var(--text-muted); }

  .card-actions {
    display: flex;
    gap: var(--space-2);
    flex-shrink: 0;
  }

  .action-btn {
    display: flex;
    align-items: center;
    gap: var(--space-1);
    padding: var(--space-1) var(--space-3);
    border-radius: var(--radius-sm);
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-medium);
    color: var(--text-secondary);
  }
  .action-btn:hover { color: var(--text-primary); }

  .load-btn:hover { color: var(--accent); }
  .delete-btn:hover { color: var(--error); }

  .delete-confirm-btn {
    background: var(--error-muted);
    border-color: var(--error);
    color: var(--error);
  }
  .delete-confirm-btn:hover {
    background: var(--error);
    color: var(--text-inverse);
  }

  /* Dialog */
  .dialog-backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.5);
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

  .dialog-title { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-primary); margin: 0; }

  .dialog-close {
    padding: var(--space-2);
    border-radius: var(--radius-sm);
    color: var(--text-muted);
  }
  .dialog-close:hover { color: var(--text-primary); }

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
  .tab-active { color: var(--accent); border-bottom-color: var(--accent); }

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

  .general-view { display: flex; flex-direction: column; gap: var(--space-6); }
  .section-title { font-size: var(--font-size-xs); font-weight: var(--font-weight-semibold); color: var(--text-muted); text-transform: uppercase; letter-spacing: 0.5px; margin: 0 0 var(--space-3) 0; }

  .preset-profiles { display: flex; flex-direction: column; gap: var(--space-3); }

  .kv-card {
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-md);
  }

  .kv-card-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: var(--space-2);
  }

  .kv-card-name { font-size: var(--font-size-sm); font-weight: var(--font-weight-semibold); color: var(--text-primary); }
  .kv-card-mode { font-size: var(--font-size-xs); color: var(--text-muted); font-family: var(--font-mono); }

  .kv-card-details {
    display: flex;
    gap: var(--space-5);
    font-size: var(--font-size-xs);
    color: var(--text-secondary);
    font-family: var(--font-mono);
  }

  .kv-grid { display: flex; flex-direction: column; gap: var(--space-2); }
  .kv { display: flex; justify-content: space-between; align-items: center; padding: var(--space-2) var(--space-3); border-radius: var(--radius-sm); background: var(--glass-bg); }
  .kv-key { font-size: var(--font-size-sm); color: var(--text-secondary); }
  .kv-val { font-size: var(--font-size-sm); font-family: var(--font-mono); color: var(--text-primary); }
  .empty-note { font-size: var(--font-size-sm); color: var(--text-muted); font-style: italic; }

  .placeholder { display: flex; flex-direction: column; align-items: center; text-align: center; gap: var(--space-4); padding: var(--space-10) 0; }
  .placeholder h3 { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-secondary); margin: 0; }
  .placeholder p { font-size: var(--font-size-sm); color: var(--text-muted); max-width: 320px; margin: 0; }

  /* Save dialog */
  .save-dialog { max-width: 440px; }

  .save-description {
    font-size: var(--font-size-sm);
    color: var(--text-secondary);
    margin: 0 0 var(--space-5) 0;
    line-height: var(--line-height-normal);
  }

  .save-field {
    display: flex;
    flex-direction: column;
    gap: var(--space-2);
  }

  .save-label {
    font-size: var(--font-size-xs);
    font-weight: var(--font-weight-medium);
    color: var(--text-muted);
    text-transform: uppercase;
    letter-spacing: 0.5px;
  }

  .save-input {
    padding: var(--space-3) var(--space-4);
    border-radius: var(--radius-sm);
    background: var(--glass-bg);
    border: 1px solid var(--glass-border);
    color: var(--text-primary);
    font-size: var(--font-size-base);
    outline: none;
    transition: border-color var(--transition-fast);
  }

  .save-input:focus { border-color: var(--accent); }
  .save-input::placeholder { color: var(--text-muted); }

  .save-error {
    font-size: var(--font-size-xs);
    color: var(--error);
    margin: var(--space-2) 0 0 0;
  }

  .save-actions {
    display: flex;
    justify-content: flex-end;
    gap: var(--space-3);
    margin-top: var(--space-6);
  }

  .cancel-btn, .confirm-btn {
    padding: var(--space-2) var(--space-5);
    border-radius: var(--radius-sm);
    font-size: var(--font-size-sm);
    font-weight: var(--font-weight-medium);
  }

  .confirm-btn {
    background: var(--accent-muted);
    border-color: var(--accent);
    color: var(--accent);
  }

  .confirm-btn:hover:not(:disabled) {
    background: var(--accent);
    color: var(--text-inverse);
  }

  .confirm-btn:disabled {
    opacity: 0.4;
    cursor: not-allowed;
  }

  @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
  @keyframes scaleIn { from { opacity: 0; transform: scale(0.95); } to { opacity: 1; transform: scale(1); } }
</style>
