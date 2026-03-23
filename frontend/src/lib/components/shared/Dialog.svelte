<script lang="ts">
  import type { Snippet } from 'svelte';

  let {
    title,
    onclose,
    children,
    wide = false,
  }: {
    title: string;
    onclose: () => void;
    children: Snippet;
    wide?: boolean;
  } = $props();

  function handleBackdrop(e: MouseEvent) {
    if (e.target === e.currentTarget) onclose();
  }

  function handleKeydown(e: KeyboardEvent) {
    if (e.key === 'Escape') onclose();
  }
</script>

<svelte:window onkeydown={handleKeydown} />

<!-- svelte-ignore a11y_no_static_element_interactions -->
<div class="backdrop" onclick={handleBackdrop}>
  <div class="dialog glass-heavy" class:wide>
    <div class="dialog-header">
      <h2 class="dialog-title">{title}</h2>
      <button class="dialog-close glass glass-interactive" onclick={onclose}>
        <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
          <line x1="18" y1="6" x2="6" y2="18"/>
          <line x1="6" y1="6" x2="18" y2="18"/>
        </svg>
      </button>
    </div>
    {@render children()}
  </div>
</div>

<style>
  .backdrop {
    position: fixed;
    inset: 0;
    background: rgba(0, 0, 0, 0.12);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 100;
    animation: fadeIn 0.15s ease;
  }

  .dialog {
    width: 90%;
    max-width: 440px;
    max-height: 80vh;
    display: flex;
    flex-direction: column;
    animation: scaleIn 0.2s cubic-bezier(0.34, 1.56, 0.64, 1);
  }

  .dialog.wide {
    max-width: 700px;
  }

  .dialog-header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    padding: var(--space-5) var(--space-6);
    border-bottom: 1px solid var(--divider);
  }

  .dialog-title {
    font-size: var(--font-size-lg);
    font-weight: var(--font-weight-semibold);
    color: var(--text-primary);
    margin: 0;
  }

  .dialog-close {
    padding: var(--space-2);
    border-radius: var(--radius-sm);
    color: var(--text-muted);
  }

  .dialog-close:hover {
    color: var(--text-primary);
  }

  @keyframes fadeIn { from { opacity: 0; } to { opacity: 1; } }
  @keyframes scaleIn { from { opacity: 0; transform: scale(0.95); } to { opacity: 1; transform: scale(1); } }
</style>
