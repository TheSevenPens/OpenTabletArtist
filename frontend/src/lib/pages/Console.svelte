<script lang="ts">
  import PageHeader from '../components/shared/PageHeader.svelte';
  import Placeholder from '../components/shared/Placeholder.svelte';
</script>

<div class="page">
  <PageHeader title="Console" subtitle="Driver log output" />

  <Placeholder title="Coming Soon" description="Live log streaming from the OpenTabletDriver daemon will appear here.">
    {#snippet icon()}
      <svg width="48" height="48" viewBox="0 0 24 24" fill="none" stroke="var(--text-muted)" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
        <polyline points="4 17 10 11 4 5"/><line x1="12" y1="19" x2="20" y2="19"/>
      </svg>
    {/snippet}
  </Placeholder>
</div>

<style>
  .page { max-width: 900px; }
</style>
