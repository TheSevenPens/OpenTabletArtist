<script lang="ts">
  import GlassPanel from '../components/shared/GlassPanel.svelte';
  import PageHeader from '../components/shared/PageHeader.svelte';
</script>

<div class="page">
  <PageHeader title="About" subtitle="Driver UX Experiment" />

  <GlassPanel>
    <div class="about-section">
      <h3>What is this?</h3>
      <p>A UX prototype exploring a ideas in simplifying the experience for Tablet drivers. Currently interfacing with OpenTabletDriver — an open-source, cross-platform tablet driver.</p>
    </div>
  </GlassPanel>
</div>

<style>
  .page { max-width: 900px; }

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
</style>
