<script lang="ts">
  import GlassPanel from '../shared/GlassPanel.svelte';
  import type { Profile } from '../../types/settings';
  import { getPluginShortName } from '../../utils/plugin';

  let { profile, onopen, onforget }: {
    profile: Profile;
    onopen?: (profile: Profile) => void;
    onforget?: (profile: Profile) => void;
  } = $props();
</script>

<GlassPanel interactive>
  <div class="card-wrapper">
    <a href="#/tablets/{encodeURIComponent(profile.tablet)}/area" class="card-link">
      <div class="card-icon">
        <svg width="24" height="24" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <rect x="4" y="2" width="16" height="20" rx="2"/>
          <line x1="12" y1="18" x2="12.01" y2="18"/>
        </svg>
      </div>

      <div class="card-info">
        <h3 class="card-name">{profile.tablet}</h3>
        <span class="card-mode">{getPluginShortName(profile.outputMode, 'No output mode')}</span>
      </div>

      {#if onopen}
        <button class="action-btn glass glass-interactive" onclick={(e) => { e.preventDefault(); e.stopPropagation(); onopen(profile); }}>
          <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
            <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
            <polyline points="14 2 14 8 20 8"/>
          </svg>
          Open
        </button>
      {/if}
    </a>

    {#if onforget}
      <button
        class="forget-btn"
        title="Forget this tablet's settings"
        onclick={(e) => { e.preventDefault(); e.stopPropagation(); onforget(profile); }}
      >
        <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <line x1="18" y1="6" x2="6" y2="18"/>
          <line x1="6" y1="6" x2="18" y2="18"/>
        </svg>
      </button>
    {/if}
  </div>
</GlassPanel>

<style>
  .card-wrapper {
    position: relative;
  }

  .card-link {
    display: flex;
    align-items: center;
    gap: var(--space-4);
    text-decoration: none;
    color: inherit;
  }

  .card-icon {
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

  .card-info {
    flex: 1;
    min-width: 0;
  }

  .card-name {
    font-size: var(--font-size-base);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0 0 var(--space-1) 0;
  }

  .card-mode {
    font-size: var(--font-size-xs);
    color: var(--text-muted);
  }

  .action-btn {
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

  .action-btn:hover {
    color: var(--text-primary);
  }

  .forget-btn {
    position: absolute;
    top: -4px;
    right: -4px;
    width: 28px;
    height: 28px;
    display: flex;
    align-items: center;
    justify-content: center;
    border-radius: var(--radius-full);
    background: var(--glass-bg);
    border: 1px solid var(--glass-border);
    color: var(--text-muted);
    cursor: pointer;
    opacity: 0;
    transition: all var(--transition-fast);
  }

  .card-wrapper:hover .forget-btn {
    opacity: 1;
  }

  .forget-btn:hover {
    color: var(--error);
    border-color: var(--error);
    background: var(--error-muted, rgba(239, 68, 68, 0.1));
  }
</style>
