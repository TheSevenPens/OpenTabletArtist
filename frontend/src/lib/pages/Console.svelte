<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
</script>

<div class="page">
  <header class="page-header">
    <h1 class="page-title">Console</h1>
    <p class="page-subtitle">Driver log output</p>
  </header>

  <GlassPanel>
    <div class="placeholder">
      <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/>
      </svg>
      <h3>Coming Soon</h3>
      <p>Live log streaming from the OpenTabletDriver daemon will appear here.</p>
    </div>
  </GlassPanel>
</div>

<style>
  .page { max-width: 900px; }
  .page-header { margin-bottom: var(--space-7); }
  .page-title { font-size: var(--font-size-2xl); font-weight: var(--font-weight-bold); color: var(--text-primary); margin: 0 0 var(--space-1) 0; }
  .page-subtitle { font-size: var(--font-size-base); color: var(--text-secondary); margin: 0; }
  .placeholder { display: flex; flex-direction: column; align-items: center; text-align: center; gap: var(--space-4); padding: var(--space-10) 0; }
  .placeholder h3 { font-size: var(--font-size-lg); font-weight: var(--font-weight-semibold); color: var(--text-secondary); margin: 0; }
  .placeholder p { font-size: var(--font-size-sm); color: var(--text-muted); max-width: 320px; margin: 0; }
</style>
