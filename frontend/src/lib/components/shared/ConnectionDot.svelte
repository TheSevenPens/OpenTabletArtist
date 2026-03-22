<script lang="ts">
  import { connectionStore } from '../../stores/connection.svelte';
</script>

<div class="dot-wrapper">
  <div class="dot" class:connected={connectionStore.status === 'connected'} class:connecting={connectionStore.status === 'connecting'} class:disconnected={connectionStore.status === 'disconnected'}></div>
  <span class="label">{connectionStore.status}</span>
</div>

<style>
  .dot-wrapper {
    display: flex;
    align-items: center;
    gap: var(--space-2);
  }

  .dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    transition: background var(--transition-smooth), box-shadow var(--transition-smooth);
  }

  .connected {
    background: var(--success);
    box-shadow: 0 0 8px var(--success);
  }

  .connecting {
    background: var(--warning);
    box-shadow: 0 0 8px var(--warning);
    animation: pulse 1.5s ease-in-out infinite;
  }

  .disconnected {
    background: var(--text-muted);
  }

  .label {
    font-size: var(--font-size-xs);
    color: var(--text-secondary);
    text-transform: capitalize;
  }

  @keyframes pulse {
    0%, 100% { opacity: 1; }
    50% { opacity: 0.4; }
  }
</style>
