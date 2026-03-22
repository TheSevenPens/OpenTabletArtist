<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
</script>

<div class="page">
  <header class="page-header">
    <h1 class="page-title">Filters</h1>
    <p class="page-subtitle">Input processing pipeline</p>
  </header>

  <GlassPanel>
    <div class="placeholder">
      <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
        <polygon points="22 3 2 3 10 12.46 10 19 14 21 14 12.46 22 3"/>
      </svg>
      <h3>Coming Soon</h3>
      <p>Smoothing, anti-chatter, and other input filters will be configurable here.</p>
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
