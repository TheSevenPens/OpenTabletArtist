<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import PageHeader from '../components/shared/PageHeader.svelte';
</script>

<div class="page">
  <PageHeader title="About" subtitle="OpenTabletDriver UX Prototype" />

  <div class="about-grid">
    <GlassPanel>
      <div class="about-section">
        <h3>What is this?</h3>
        <p>A UX prototype exploring a new visual experience for OpenTabletDriver — an open-source, cross-platform tablet driver.</p>
      </div>
    </GlassPanel>

    <GlassPanel>
      <div class="about-section">
        <h3>Architecture</h3>
        <div class="arch-stack">
          <div class="arch-item glass-subtle">Svelte 5 + Vite</div>
          <div class="arch-arrow">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><polyline points="19 12 12 19 5 12"/></svg>
          </div>
          <div class="arch-item glass-subtle">.NET Bridge (REST + WS)</div>
          <div class="arch-arrow">
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="2"><line x1="12" y1="5" x2="12" y2="19"/><polyline points="19 12 12 19 5 12"/></svg>
          </div>
          <div class="arch-item glass-subtle">OTD Daemon (Named Pipe)</div>
        </div>
      </div>
    </GlassPanel>
  </div>
</div>

<style>
  .page { max-width: 900px; }

  .about-grid {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: var(--space-5);
  }

  .about-section h3 {
    font-size: var(--font-size-base);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0 0 var(--space-3) 0;
  }

  .about-section p {
    font-size: var(--font-size-sm);
    color: var(--text-secondary);
    line-height: var(--line-height-normal);
    margin: 0;
  }

  .arch-stack { display: flex; flex-direction: column; align-items: center; gap: var(--space-2); }
  .arch-item { padding: var(--space-2) var(--space-4); font-size: var(--font-size-sm); font-weight: var(--font-weight-medium); color: var(--text-primary); text-align: center; width: 100%; }
  .arch-arrow { display: flex; align-items: center; justify-content: center; }
</style>
