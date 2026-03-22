<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import { settingsStore } from '../stores/settings.svelte';
</script>

<div class="page">
  <header class="page-header">
    <h1 class="page-title">Profiles</h1>
    <p class="page-subtitle">Manage per-tablet configurations</p>
  </header>

  {#if settingsStore.current?.profiles?.length}
    <div class="profiles-list">
      {#each settingsStore.current.profiles as profile}
        <GlassPanel interactive>
          <div class="profile-card">
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
          </div>
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

<style>
  .page { max-width: 900px; }
  .page-header { margin-bottom: var(--space-7); }
  .page-title { font-size: var(--font-size-2xl); font-weight: var(--font-weight-bold); color: var(--text-primary); margin: 0 0 var(--space-1) 0; }
  .page-subtitle { font-size: var(--font-size-base); color: var(--text-secondary); margin: 0; }

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

  .placeholder { display: flex; flex-direction: column; align-items: center; text-align: center; gap: var(--space-4); padding: var(--space-10) 0; }
  .placeholder h3 { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-secondary); margin: 0; }
  .placeholder p { font-size: var(--font-size-sm); color: var(--text-muted); max-width: 320px; margin: 0; }
</style>
